﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using TeamTalentMgmtApp.Shared.Models.DatabaseContext;

namespace TeamTalentMgmtApp.Shared.Services.Interfaces
{
    public interface IRecruiterService
    {
        Task<Recruiter> GetById(int id, CancellationToken cancellationToken = default);
        Task<ReadOnlyCollection<Recruiter>> GetAllHiringManagers(CancellationToken cancellationToken = default);
        Task<ReadOnlyCollection<Recruiter>> GetAllInterviewers(CancellationToken cancellationToken = default);
        Task SaveConversationData(string serviceUrl, string tenantId, Dictionary<string, string> channelAccountsEmails, CancellationToken cancellationToken = default);
    }
}
