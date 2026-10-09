namespace AutoDuty.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Configurations;
    using Dalamud.Plugin.Services;
    using ECommons.DalamudServices;
    using ECommons.ExcelServices;
    using ECommons.GameHelpers;
    using ECommons.Throttlers;
    using FFXIVClientStructs.FFXIV.Client.Game;
    using IPC;
    using Lumina.Excel.Sheets;

    /// <summary>
    /// Completes the currently available class / job (and optionally role) quests of the current job through Questionable.
    /// </summary>
    public class QuestionableJobQuestHelper : ActiveHelperBase<QuestionableJobQuestHelper, JobQuestLoopActionConfig>
    {
        public override string    Name               { get; }       = nameof(QuestionableJobQuestHelper);
        public override string    DisplayNameKey     { get; }       = "LoopActions.JobQuest.Name";
        public override string[]? Commands           { get; init; } = ["jobquest", "jq"];
        public override string?   CommandDescription { get; init; } = "Completes available class/job quests of your current job using Questionable";

        // Timeouts are handled per quest instead
        protected override int TimeOut { get; set; } = 0;

        private const int  START_TIMEOUT_MS            = 15_000;
        private const int  STOP_TIMEOUT_MS             = 10_000;
        private const int  NOT_RUNNING_GRACE_MS        = 3_000;
        private const uint ROLE_QUEST_JOURNAL_CATEGORY = 95;

        private readonly record struct JobQuest(uint RowId, ushort Level, bool RoleQuest);

        private enum Phase
        {
            Idle,
            Starting,
            Running,
            /// <summary>Waiting for Questionable to actually stop before anything else is done.</summary>
            Stopping
        }

        /// <summary>Quests that Questionable failed to complete this session, so they're not retried every loop.</summary>
        private static readonly HashSet<uint> failedQuests = [];

        private static readonly Dictionary<Job, List<JobQuest>> questCache = [];
        private static          uint?                           classJobSection;
        private static          (Job job, PropertyInfo? prop)[]? jobProperties;

        private Phase     phase = Phase.Idle;
        private uint      currentQuest;
        private DateTime  phaseStarted;
        private DateTime  questStarted;
        private DateTime? notRunningSince;
        private DateTime  stopRequested;

        internal override void Start()
        {
            if (!Questionable_IPCSubscriber.IsEnabled)
            {
                Svc.Log.Info("Job Quests requires the Questionable plugin");
                return;
            }

            if (State == ActionState.Running)
                return;

            // Skipping here would let the following loop actions (e.g. retiring to the inn) run while Questionable is
            // still moving the character, both sides then keep cancelling each other's teleports
            bool questionableRunning = Questionable_IPCSubscriber.IsRunning;

            if (!questionableRunning && FindNextQuest(this.ActionConfig.IncludeRoleQuests) == null)
            {
                this.DebugLog("No job quests available");
                return;
            }

            this.ResetState();
            base.Start();

            if (questionableRunning)
            {
                this.InfoLog("Questionable is already running, stopping it before doing job quests");
                this.StopQuestionable(DateTime.Now);
            }
        }

        internal override void Stop()
        {
            if (this.phase != Phase.Idle && Questionable_IPCSubscriber.IsRunning)
                Questionable_IPCSubscriber.Stop();

            this.ResetState();
            this.stopRequested = DateTime.Now;
            base.Stop();
        }

        /// <summary>
        /// Only report the helper as finished once Questionable has actually stopped, otherwise the next loop action
        /// starts while Questionable is still running.
        /// </summary>
        protected override void HelperStopUpdate(IFramework framework)
        {
            if (Questionable_IPCSubscriber.IsRunning)
            {
                if ((DateTime.Now - this.stopRequested).TotalMilliseconds < STOP_TIMEOUT_MS)
                {
                    if (EzThrottler.Throttle($"{this.Name}-StopQuestionable", 1000))
                        Questionable_IPCSubscriber.Stop();
                    return;
                }

                if (EzThrottler.Throttle($"{this.Name}-StopQuestionableTimeout", 60_000))
                    this.InfoLog("Questionable did not stop in time, finishing anyway");
            }

            base.HelperStopUpdate(framework);
        }

        private void ResetState()
        {
            this.phase           = Phase.Idle;
            this.currentQuest    = 0;
            this.notRunningSince = null;
        }

        private void StopQuestionable(DateTime now)
        {
            Questionable_IPCSubscriber.Stop();
            this.phase           = Phase.Stopping;
            this.phaseStarted    = now;
            this.notRunningSince = null;
        }

        protected override unsafe void HelperUpdate(IFramework framework)
        {
            if (!EzThrottler.Throttle(this.Name, 500))
                return;

            if (!PlayerHelper.IsValid)
                return;

            if (!Questionable_IPCSubscriber.IsEnabled)
            {
                this.InfoLog("Questionable is not available anymore");
                this.Stop();
                return;
            }

            DateTime now = DateTime.Now;

            switch (this.phase)
            {
                case Phase.Idle:
                {
                    if (!PlayerHelper.IsReadyFull)
                        return;

                    // never run anything alongside a Questionable that got (re)started by something else
                    if (Questionable_IPCSubscriber.IsRunning)
                    {
                        this.InfoLog("Questionable is running unexpectedly, stopping it before continuing");
                        this.StopQuestionable(now);
                        return;
                    }

                    uint? next = FindNextQuest(this.ActionConfig.IncludeRoleQuests);
                    if (next == null)
                    {
                        this.InfoLog("No more job quests available");
                        this.Stop();
                        return;
                    }

                    if (!Questionable_IPCSubscriber.StartSingleQuest(next.Value))
                    {
                        this.InfoLog($"Questionable refused to start quest {QuestName(next.Value)} ({next.Value}), skipping it");
                        failedQuests.Add(next.Value);
                        return;
                    }

                    this.InfoLog($"Started quest {QuestName(next.Value)} ({next.Value})");
                    this.currentQuest    = next.Value;
                    this.phase           = Phase.Starting;
                    this.phaseStarted    = now;
                    this.questStarted    = now;
                    this.notRunningSince = null;
                    Plugin.action        = Loc.Get("LoopActions.JobQuest.Doing", QuestName(next.Value));
                    break;
                }
                case Phase.Starting:
                {
                    if (Questionable_IPCSubscriber.IsRunning)
                    {
                        this.phase = Phase.Running;
                    }
                    else if (QuestManager.IsQuestComplete(this.currentQuest))
                    {
                        this.phase = Phase.Idle;
                    }
                    else if ((now - this.phaseStarted).TotalMilliseconds > START_TIMEOUT_MS)
                    {
                        this.InfoLog($"Questionable did not start quest {this.currentQuest}, skipping it");
                        failedQuests.Add(this.currentQuest);
                        this.StopQuestionable(now);
                    }
                    break;
                }
                case Phase.Running:
                {
                    if (this.ActionConfig.TimeoutMinutes > 0 && (now - this.questStarted).TotalMinutes > this.ActionConfig.TimeoutMinutes)
                    {
                        this.InfoLog($"Quest {this.currentQuest} timed out, skipping it");
                        failedQuests.Add(this.currentQuest);
                        this.StopQuestionable(now);
                        return;
                    }

                    if (Questionable_IPCSubscriber.IsRunning)
                    {
                        this.notRunningSince = null;
                        return;
                    }

                    // Questionable may pause shortly between steps, only treat it as finished after a grace period
                    this.notRunningSince ??= now;
                    if ((now - this.notRunningSince.Value).TotalMilliseconds < NOT_RUNNING_GRACE_MS)
                        return;

                    if (QuestManager.IsQuestComplete(this.currentQuest))
                    {
                        this.InfoLog($"Completed quest {QuestName(this.currentQuest)} ({this.currentQuest})");
                    }
                    else
                    {
                        this.InfoLog($"Questionable stopped without completing quest {this.currentQuest}, skipping it");
                        failedQuests.Add(this.currentQuest);
                    }

                    this.phase = Phase.Idle;
                    break;
                }
                case Phase.Stopping:
                {
                    if (Questionable_IPCSubscriber.IsRunning)
                    {
                        this.notRunningSince = null;
                        if ((now - this.phaseStarted).TotalMilliseconds > STOP_TIMEOUT_MS)
                        {
                            this.InfoLog("Questionable is still running, asking it to stop again");
                            this.StopQuestionable(now);
                        }
                        return;
                    }

                    // make sure it stays stopped before the next quest or loop action takes over
                    this.notRunningSince ??= now;
                    if ((now - this.notRunningSince.Value).TotalMilliseconds < NOT_RUNNING_GRACE_MS)
                        return;

                    this.phase = Phase.Idle;
                    break;
                }
            }
        }

        internal static bool AnyQuestAvailable(bool includeRoleQuests) =>
            Questionable_IPCSubscriber.IsEnabled && FindNextQuest(includeRoleQuests) != null;

        internal static void ClearFailedQuests() =>
            failedQuests.Clear();

        internal static int FailedQuestCount => failedQuests.Count;

        private static uint? FindNextQuest(bool includeRoleQuests)
        {
            if (!PlayerHelper.IsValid)
                return null;

            int level = Player.Level;

            JobQuest[] candidates = GetJobQuests(Player.Job)
                                   .Where(q => q.Level <= level && (includeRoleQuests || !q.RoleQuest) &&
                                               !failedQuests.Contains(q.RowId) && !QuestManager.IsQuestComplete(q.RowId))
                                   .ToArray();

            foreach (JobQuest quest in candidates)
                if (Questionable_IPCSubscriber.IsQuestAccepted(quest.RowId))
                    return quest.RowId;

            foreach (JobQuest quest in candidates)
                if (Questionable_IPCSubscriber.IsReadyToAcceptQuest(quest.RowId))
                    return quest.RowId;

            return null;
        }

        private static string QuestName(uint rowId) =>
            Svc.Data.GetExcelSheet<Quest>().GetRowOrDefault(rowId)?.Name.ToString() ?? rowId.ToString();

        /// <summary>
        /// The journal section containing class and job quests, determined from the class/job unlock quests
        /// instead of a hardcoded id so it keeps working across clients and patches.
        /// </summary>
        private static uint ClassJobSection =>
            classJobSection ??= Svc.Data.GetExcelSheet<ClassJob>()
                                   .Select(cj => cj.UnlockQuest.ValueNullable?.JournalGenre.ValueNullable?.JournalCategory.ValueNullable?.JournalSection.RowId ?? 0u)
                                   .Where(id => id != 0)
                                   .GroupBy(id => id)
                                   .OrderByDescending(g => g.Count())
                                   .Select(g => g.Key)
                                   .FirstOrDefault();

        private static (Job job, PropertyInfo? prop)[] JobProperties =>
            jobProperties ??= Enum.GetValues<Job>()
                                  .Where(j => j != Job.ADV)
                                  .Select(j => (j, typeof(ClassJobCategory).GetProperty(j.ToString(), BindingFlags.Public | BindingFlags.Instance)))
                                  .Where(t => t.Item2?.PropertyType == typeof(bool))
                                  .ToArray();

        private static List<JobQuest> GetJobQuests(Job job)
        {
            if (questCache.TryGetValue(job, out List<JobQuest>? cached))
                return cached;

            List<JobQuest> quests = [];

            PropertyInfo? jobProperty = JobProperties.FirstOrDefault(t => t.job == job).prop;
            uint          section     = ClassJobSection;

            if (jobProperty != null && section != 0)
            {
                Dictionary<uint, int> jobCountCache = [];

                foreach (Quest quest in Svc.Data.GetExcelSheet<Quest>())
                {
                    if (quest.Name.IsEmpty || quest.IsRepeatable)
                        continue;

                    JournalCategory? journalCategory = quest.JournalGenre.ValueNullable?.JournalCategory.ValueNullable;
                    if (journalCategory?.JournalSection.RowId != section)
                        continue;

                    ClassJobCategory? category = quest.ClassJobCategory0.ValueNullable;
                    if (category == null || !(bool)jobProperty.GetValue(category.Value)!)
                        continue;

                    if (!jobCountCache.TryGetValue(category.Value.RowId, out int jobCount))
                    {
                        ClassJobCategory cat = category.Value;
                        jobCount = JobProperties.Count(t => (bool)t.prop!.GetValue(cat)!);
                        jobCountCache[cat.RowId] = jobCount;
                    }

                    // Class and job quests are restricted to the class and/or its job. Quests shared between many jobs are
                    // either role quests or unlock / relic quests of other classes and jobs, only the former are wanted.
                    bool roleQuest = journalCategory.Value.RowId == ROLE_QUEST_JOURNAL_CATEGORY;
                    if (jobCount > 2 && !roleQuest)
                        continue;

                    quests.Add(new JobQuest(quest.RowId, quest.ClassJobLevel[0], roleQuest));
                }

                quests.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.RowId.CompareTo(b.RowId));
            }

            Svc.Log.Debug($"QuestionableJobQuestHelper: found {quests.Count} class/job quests for {job} in journal section {section}");
            questCache[job] = quests;
            return quests;
        }
    }
}
