using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdaptiveCards;
using Azure;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Options;
using TeamsTalentMgmtApp.Constants;
using TeamsTalentMgmtApp.Models;
using TeamsTalentMgmtApp.Models.DatabaseContext;
using TeamsTalentMgmtApp.Models.TemplateModels;
using TeamsTalentMgmtApp.Services.Interfaces;
using TeamsTalentMgmtApp.Services.Templates;

namespace TeamsTalentMgmtApp.Controllers
{
    public class NotificationService : INotificationService
    {
        private readonly IGraphApiService _graphApiService;
        private readonly AppSettings _appSettings;
        private readonly IBotFrameworkHttpAdapter _adapter;
        private readonly IRecruiterService _recruiterService;
        private readonly PositionsTemplate _positionsTemplate;
        private readonly CandidatesTemplate _candidatesTemplate;
        private readonly ITokenProvider _tokenProvider;

        public NotificationService(
            IGraphApiService graphApiService, 
            IOptions<AppSettings> appSettings, 
            IBotFrameworkHttpAdapter adapter, 
            IRecruiterService recruiterService,
            PositionsTemplate positionsTemplate,
            CandidatesTemplate candidatesTemplate,
            ITokenProvider tokenProvider)
        {
            _graphApiService = graphApiService;
            _appSettings = appSettings.Value;
            _adapter = adapter;
            _recruiterService = recruiterService;
            _positionsTemplate = positionsTemplate;
            _candidatesTemplate = candidatesTemplate;
            _tokenProvider = tokenProvider;
        }

        public async Task NotifyRecruiterAboutCandidateStageChange(string tenantId, Candidate candidate, CancellationToken cancellationToken)
        {
            if (candidate?.Position != null)
            {
                var recruiter = await _recruiterService.GetById(candidate.Position.HiringManagerId, cancellationToken);

                var interviewers = await _recruiterService.GetAllInterviewers(cancellationToken);
                var templateModel = new CandidateTemplateModel
                {
                    Items = new List<Candidate> { candidate },
                    Interviewers = interviewers,
                    AppSettings = _appSettings
                };

                var attachments = (await _candidatesTemplate.RenderTemplate(null, null, TemplateConstants.CandidateAsAdaptiveCardWithMultipleItems, templateModel)).Attachments;

                var activity = MessageFactory.Text($"Candidate stage has been changed for {candidate.Name} from {candidate.PreviousStage} to {candidate.Stage}");

                activity.Attachments = attachments;

                await SendProactiveNotification(recruiter.Alias, tenantId, activity, cancellationToken);
            }
        }

        public async Task NotifyRecruiterAboutNewOpenPosition(string tenantId, Position position, CancellationToken cancellationToken)
        {
            var recruiter = await _recruiterService.GetById(position.HiringManagerId, cancellationToken);

            var staticTabName = "Potential candidates";

            var positionsTemplate = new PositionTemplateModel
            {
                Items = new List<Position> { position },
                ButtonActions = new List<AdaptiveAction>
                {
                    new AdaptiveOpenUrlAction
                    {
                        Title = "Show all assigned positions",
                        Url = new Uri(string.Format(CommonConstants.DeepLinkUrlFormat, _appSettings.TeamsAppId, _appSettings.OpenPositionsTabEntityId, staticTabName))
                    }
                }
            };

            var attachments = (await _positionsTemplate.RenderTemplate(null, null, TemplateConstants.PositionAsAdaptiveCardWithMultipleItems, positionsTemplate)).Attachments;

            var activity = MessageFactory.Attachment(attachments);

            await SendProactiveNotification(recruiter.Alias, tenantId, activity, cancellationToken);
        }

        public async Task<NotificationResult> SendProactiveChannelNotification(string channelId, IActivity activityToSend, CancellationToken cancellationToken = default)
        {

            //stored creds for use with connector client
            var credentials = new MicrosoftAppCredentials(_appSettings.MicrosoftAppId, _appSettings.MicrosoftAppPassword);

            //setting conversationparameters for messaging a user 1 on 1 with the bot
            var conversationParameters = new ConversationParameters
            {
                IsGroup = true,
                ChannelData = new { channel = new {id = channelId } },
                Activity = (Activity)activityToSend,
            };

            //uses CloudAdapter to create the conversation and awaits a response

            try
            {
                await ((CloudAdapter)_adapter).CreateConversationAsync(credentials.MicrosoftAppId, channelId, _appSettings.ServiceUrl, credentials.OAuthScope, conversationParameters, (t1, c1) =>
                {

                    var conversationReference = t1.Activity.GetConversationReference();
                    return Task.CompletedTask;


                    //commented out - but you can use ContinueConversationAsync if you want to reply to a specific thread, but you need to find the original activity. Use Graph API (or keep track of the theadID in persistant storage) to grab the Thread ID and then feed it into the Activity
                    //await ((CloudAdapter)_adapter).ContinueConversationAsync(credentials.MicrosoftAppId, conversationReference, async (t2, c2) =>
                    //{
                    //    await t2.SendActivityAsync(activityToSend, c2);
                    //}, cancellationToken);
                }, cancellationToken);

                return NotificationResult.Success;
            }
            catch (ErrorResponseException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return NotificationResult.BotNotInstalled;
            }
            catch
            {
                return NotificationResult.Failed;
            }   
        }



        //todo: Make Group Notificaitons work:

        public async Task<NotificationResult> SendGroupProactiveNotification(string[] upns, string tenantId, IActivity activity, CancellationToken cancellationToken)
        {

            
            
            //check if request contains OIDs and if not, change UPNs or Aliases to OIDs via Graph API Call. This returns all user OIDs, in an array. If null, user cannot be found.
            var chatId = await _graphApiService.GetGroupChatId(upns, tenantId, cancellationToken);
            
            if (chatId == null)
            {
                //todo: make this better...
                return NotificationResult.AliasNotFound;
            }



            //use ChatID to send proactive message (create conversation and reply)


            //stored creds for use with connector client
            var credentials = new MicrosoftAppCredentials(_appSettings.MicrosoftAppId, _appSettings.MicrosoftAppPassword);

            //initalise connectorclient which is the wrapper around v3 Bot Service endpoint
            //var connectorClient = new ConnectorClient(new Uri(_appSettings.ServiceUrl), credentials);

            //get conversation members for ChatId (making call to v3/conversation/members in bot service and store in members variable
            //var members = await connectorClient.Conversations.GetConversationMembersAsync(chatId);

            var conversationParameters = new ConversationParameters
            {
                IsGroup = true,
                ChannelData = new { channel = new { id = chatId } },
                Activity = (Activity)activity,
            };
            //todo: getting bad request - likely that conversationParameters are not correct. Need to fix!
            //uses CloudAdapter to create the conversation and awaits a response
            await ((CloudAdapter)_adapter).CreateConversationAsync(credentials.MicrosoftAppId, chatId, _appSettings.ServiceUrl, credentials.OAuthScope, conversationParameters, async (t1, c1) =>
            {
                var conversationReference = t1.Activity.GetConversationReference();
                await ((CloudAdapter)_adapter).ContinueConversationAsync(credentials.MicrosoftAppId, conversationReference, async (t2, c2) =>
                {
                    await t2.SendActivityAsync(activity, c2);
                }, cancellationToken);
            }, cancellationToken);

            return NotificationResult.Success;
        }




        //end












        public async Task<NotificationResult> SendProactiveNotification(string aliasUpnOrOid, string tenantId, IActivity activity, CancellationToken cancellationToken)
        {
            //Uses App Token to make call to Graph API to get ChatIDforUser and also expands the alias to a UPN
            var (upn, chatId) = await _graphApiService.GetProactiveChatIdForUser(aliasUpnOrOid, tenantId, cancellationToken);

            //if UPN is null, return with error code
            if (upn == null)
            {
                return NotificationResult.AliasNotFound;
            }

            //if chatId doesn't exist, bot isn't installed. return 412
            if (chatId == null)
            {
                return NotificationResult.BotNotInstalled;
            }

            //stored creds for use with connector client
            var credentials = new MicrosoftAppCredentials(_appSettings.MicrosoftAppId, _appSettings.MicrosoftAppPassword);

            //initalise connectorclient which is the wrapper around v3 Bot Service endpoint
            var connectorClient = new ConnectorClient(new Uri(_appSettings.ServiceUrl), credentials);

            //get conversation members for ChatId (making call to v3/conversation/members in bot service and store in members variable
            var members = await connectorClient.Conversations.GetConversationMembersAsync(chatId);

            //JACKNOTE: will need everything from here to message channel:
            //setting conversationparameters for messaging a user 1 on 1 with the bot
            var conversationParameters = new ConversationParameters
            {
                IsGroup = false,
                Bot = new ChannelAccount
                {
                    Id = "28:" + credentials.MicrosoftAppId
                },
                Members = new ChannelAccount[] { members[0] },
                TenantId = tenantId,
            };

            //uses CloudAdapter to create the conversation and awaits a response
            await ((CloudAdapter)_adapter).CreateConversationAsync(credentials.MicrosoftAppId, null, _appSettings.ServiceUrl, credentials.OAuthScope, conversationParameters, async (t1, c1) =>
            {
                var conversationReference = t1.Activity.GetConversationReference();
                await ((CloudAdapter)_adapter).ContinueConversationAsync(credentials.MicrosoftAppId, conversationReference, async (t2, c2) =>
                {
                    await t2.SendActivityAsync(activity, c2);
                }, cancellationToken);
            }, cancellationToken);

            return NotificationResult.Success;
        }
    }
}
