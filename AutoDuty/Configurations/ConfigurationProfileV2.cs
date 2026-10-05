namespace AutoDuty.Configurations;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Data;
using ECommons;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.ImGuiMethods;
using ECommons.IPC.Subscribers.RotationSolverReborn;
using Helpers;
using Multibox;
using Newtonsoft.Json;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Windows;

[JsonObject(MemberSerialization.OptOut)]
public class ConfigurationProfileV2
{
    public ConfigurationProfileV2()
    {
        this.Log        = new LogConfig(this);
        this.Overlay    = new OverlayConfig(this);
        this.DutyConfig = new DutyConfigConfig(this);
        this.Loop       = new LoopConfig(this);
        this.Meta       = new MetaConfig(this);
    }

    public MetaConfig Meta { get; set; }

    [JsonObject(MemberSerialization.OptOut)]
    public class MetaConfig(ConfigurationProfileV2 config)
    {
        [JsonIgnore]
        private readonly ConfigurationProfileV2 config = config;

        public Dictionary<uint, Dictionary<string, JobWithRole>?> PathSelectionsByPath { get; set; } = [];

        public AutoDutyMode AutoDutyModeEnum
        {
            get;
            set
            {
                field                          = value;
                Plugin.CurrentTerritoryContent = null;
                MainTab.DutySelected           = null;
                Plugin.LevelingModeEnum        = LevelingMode.None;
            }
        } = AutoDutyMode.Looping;

        public int LoopTimes { get; set; } = 1;

        public DutyMode dutyModeEnum = DutyMode.Support;

        public DutyMode DutyModeEnum
        {
            get => this.AutoDutyModeEnum switch
            {
                AutoDutyMode.Playlist => Plugin.PlaylistCurrentEntry?.DutyMode ?? this.dutyModeEnum,
                AutoDutyMode.Looping or _ => field
            };
            set
            {
                field                          = value;
                Plugin.CurrentTerritoryContent = null;
                MainTab.DutySelected           = null;
                Plugin.LevelingModeEnum        = LevelingMode.None;
            }
        }

        public bool Unsynced                       { get; set; } = false;
        public bool HideUnavailableDuties          { get; set; } = false;
        public bool PreferTrustOverSupportLeveling { get; set; } = false;
        public bool SquadronAssignLowestMembers    { get; set; } = true;

        public CrucibleConfig Crucible { get; set; } = new();

        [JsonObject(MemberSerialization.OptOut)]
        public class CrucibleConfig
        {
            public CrucibleDegree         Degree        { get; set; } = CrucibleDegree.Standard;
            public CrucibleTeamMode       TeamMode      { get; set; } = CrucibleTeamMode.Recommended;
            public CrucibleLevelingMode   LevelingMode  { get; set; } = CrucibleLevelingMode.Minus_3;
            public List<uint>             CustomTeam    { get; set; } = [];
            public List<CrucibleShopList> ShopLists     { get; set; } = [];
            public int                    ShopListIndex { get; set; } = 0;
            public List<uint>             ShopGearOrder { get; set; } = []; // legacy, only seeds ShopLists

            public bool FightPicks              { get; set; }                  = true;
            public bool Loot                    { get; set; }                  = true;
            public bool Treasure                { get; set; }                  = true;
            public bool Shop                    { get; set; }                  = true;
            public bool Rest                    { get; set; }                  = true;
            public bool Items                   { get; set; }                  = true;
            public bool RespectGearRequirements { get; set; }                  = true;

            public int RecommendedTeamCap
            {
                get;
                set => field = Math.Clamp(value, 3, 15);
            } = 15;

            public int ClassificationLock { get; set; } = 0;
        }

        [JsonObject(MemberSerialization.OptOut)]
        public class CrucibleShopList
        {
            public string     Name      { get; set; } = "Default";
            public List<Entry> Order     { get; set; } = [];

            public CrucibleShopList CompleteList()
            {
                IEnumerable<uint> uints = this.Order.Select(e => e.ID);
                foreach (uint id in CrucibleItemData.ShopItems.Where(id => !uints.Contains(id)).ToList())
                    this.Order.Add(id);

                return this;
            }

            [JsonIgnore]
            public bool IsCompleted => CrucibleItemData.ShopItems.All(id => this.Order.Any(e => e.ID == id));

            [JsonIgnore]
            public bool OrderChanged => CrucibleItemData.ShopItems.Length != this.Order.Count || CrucibleItemData.ShopItems.Where((id, index) => id != this.Order[index].ID).Any();

            [JsonObject(MemberSerialization.OptOut)]
            public class Entry
            {
                public required uint ID      { get; set; }
                public          bool Blocked { get; set; }

                [JsonIgnore]
                public CrucibleItemData.CrucibleItemCategory GetCategory =>
                    CrucibleItemData.GetCategoryOf(this.ID);

                public static implicit operator Entry(long id) => (uint)id;
                public static implicit operator Entry(uint id) => new() { ID = id };
            }
        }

        public bool ShowMainWindowOnStartup { get; set; } = false;

        public bool UseSliderInputs          { get; set; } = false;
        public bool LoopActionsOpenByDefault { get; set; } = true;
    }

    public LogConfig Log { get; set; }

    [JsonObject(MemberSerialization.OptOut)]
    public class LogConfig(ConfigurationProfileV2 config)
    {
        [JsonIgnore]
        private readonly ConfigurationProfileV2 config = config;

        public bool          AutoScroll    { get; set; } = true;
        public LogEventLevel LogEventLevel { get; set; } = LogEventLevel.Debug;
    }

    public OverlayConfig Overlay { get; set; }

    [JsonObject(MemberSerialization.OptOut)]
    public class OverlayConfig(ConfigurationProfileV2 config)
    {
        [JsonIgnore]
        private readonly ConfigurationProfileV2 config = config;

        public bool Show
        {
            get;
            set
            {
                field                  = value;
                Plugin.Overlay?.IsOpen = value;
            }
        } = true;

        public bool HideWhenStopped
        {
            get;
            set
            {
                field = value;
                if (Plugin.Overlay != null)
                    SchedulerHelper.ScheduleAction("LockOverlaySetter", () => Plugin.Overlay.IsOpen = !value || Plugin.States.HasAnyFlag(PluginState.Looping, PluginState.Navigating), () => Plugin.Overlay != null);
            }
        } = false;

        public bool Lock
        {
            get;
            set
            {
                field = value;
                if (value)
                    SchedulerHelper.ScheduleAction("LockOverlaySetter", () =>
                                                                        {
                                                                            if (!Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove))
                                                                                Plugin.Overlay.Flags |= ImGuiWindowFlags.NoMove;
                                                                        }, () => Plugin.Overlay != null);
                else
                    SchedulerHelper.ScheduleAction("LockOverlaySetter", () =>
                                                                        {
                                                                            if (Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove))
                                                                                Plugin.Overlay.Flags -= ImGuiWindowFlags.NoMove;
                                                                        }, () => Plugin.Overlay != null);
            }
        } = false;

        public bool NoBG
        {
            get;
            set
            {
                field = value;
                if (value)
                    SchedulerHelper.ScheduleAction("OverlayNoBGSetter", () =>
                                                                        {
                                                                            if (!Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground))
                                                                                Plugin.Overlay.Flags |= ImGuiWindowFlags.NoBackground;
                                                                        }, () => Plugin.Overlay != null);
                else
                    SchedulerHelper.ScheduleAction("OverlayNoBGSetter", () =>
                                                                        {
                                                                            if (Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground))
                                                                                Plugin.Overlay.Flags -= ImGuiWindowFlags.NoBackground;
                                                                        }, () => Plugin.Overlay != null);
            }
        } = false;

        public bool AnchorBottom    { get; set; } = false;
        public bool ShowDutyLoopText       { get; set; } = true;
        public bool ShowActionText         { get; set; } = true;

        public bool GoToActions { get; set; } = true;

        public LoopActions LoopActions { get; set; } = [ 
            new GCTurnInLoopActionConfig(),
            new DesynthLoopActionConfig(),
            new ExtractLoopActionConfig(),
            new RepairLoopActionConfig(),
            new AutoEquipLoopActionConfig(),
            new CofferOpenLoopActionConfig(),
            new TripleTriadUseLoopActionConfig(),
            new TripleTriadSellLoopActionConfig()
        ];
    }

    public DutyConfigConfig DutyConfig { get; set; }

    [JsonObject(MemberSerialization.OptOut)]
    public class DutyConfigConfig
    {
        [JsonIgnore]
        private readonly ConfigurationProfileV2 config;

        public DutyConfigConfig(ConfigurationProfileV2 config)
        {
            this.config = config;
            this.Stuck  = new StuckConfig(this);
        }

        public bool           AutoExitDuty                  { get; set; } = true;
        public bool           OnlyExitWhenDutyDone          { get; set; } = false;
        public bool           AutoManageRotationPluginState { get; set; } = true;
        public RotationPlugin RotationPlugin                { get; set; } = RotationPlugin.All;

        public WrathConfig Wrath { get; set; } = new();

        [JsonObject(MemberSerialization.OptOut)]
        public class WrathConfig
        {
            public bool                                AutoSetupJobs    { get; set; } = true;
            public WrathCombo.API.Enum.DPSRotationMode TargetingTank    { get; set; } = WrathCombo.API.Enum.DPSRotationMode.Highest_Max;
            public WrathCombo.API.Enum.DPSRotationMode TargetingNonTank { get; set; } = WrathCombo.API.Enum.DPSRotationMode.Lowest_Current;
        }

        public RSRConfig RSR { get; set; } = new();

        [JsonObject(MemberSerialization.OptOut)]
        public class RSRConfig
        {
            public RotationSolverRebornIPC.TargetHostileType TargetHostileType    { get; set; } = RotationSolverRebornIPC.TargetHostileType.AllTargetsCanAttack;
            public RotationSolverRebornIPC.TargetingType     TargetingTypeTank    { get; set; } = RotationSolverRebornIPC.TargetingType.HighHP;
            public RotationSolverRebornIPC.TargetingType     TargetingTypeNonTank { get; set; } = RotationSolverRebornIPC.TargetingType.LowHP;
        }

        public bool AutoManageBossModAISettings { get; set; } = true;

        public BossModConfig BossMod { get; set; } = new();

        [JsonObject(MemberSerialization.OptOut)]
        public class BossModConfig
        {
            public bool UpdatePresetsAutomatically { get; set; } = true;

            public bool MaxDistanceToTargetRoleBased
            {
                get;
                set
                {
                    field = value;
                    if (value)
                        SchedulerHelper.ScheduleAction("MaxDistanceToTargetRoleBasedBMRoleChecks", BMRoleChecks, () => PlayerHelper.IsReady);
                }
            } = true;

            public float MaxDistanceToTargetFloat    { get; set; } = 2.6f;
            public float MaxDistanceToTargetAoEFloat { get; set; } = 12;

            public bool PositionalRoleBased
            {
                get;
                set
                {
                    field = value;
                    if (value)
                        SchedulerHelper.ScheduleAction("PositionalRoleBasedBMRoleChecks", BMRoleChecks, () => PlayerHelper.IsReady);
                }
            } = true;

            public float MaxDistanceToTargetRoleMelee  { get; set; } = 2.6f;
            public float MaxDistanceToTargetRoleRanged { get; set; } = 10f;

            internal bool       PositionalAvarice { get; set; } = true;
            public   Positional PositionalEnum    { get; set; } = Positional.Any;
        }

        public  bool       AutoManageVnavAlignCamera      { get; set; } = true;
        public  bool       LootTreasure                   { get; set; } = true;
        public  LootMethod LootMethodEnum                 { get; set; } = LootMethod.AutoDuty;
        public  bool       LootBossTreasureOnly           { get; set; } = false;
        public  int        TreasureCofferScanDistance     { get; set; } = 25;

        public StuckConfig Stuck { get; set; }

        [JsonObject(MemberSerialization.OptOut)]
        public class StuckConfig(DutyConfigConfig config)
        {
            [JsonIgnore]
            private readonly DutyConfigConfig config = config;

            public bool RebuildNavmeshOnStuck          { get; set; } = true;
            public byte RebuildNavmeshAfterStuckXTimes { get; set; } = 5;
            public int  MinStuckTime                   { get; set; } = 500;
            public bool StuckOnStep                    { get; set; } = true;
            public int  StuckReturnX                   { get; set; } = 10;

            public bool StuckReturn
            {
                get => field && !MultiboxUtility.Config.MultiBox;
                set;
            } = true;
        }
        public uint WaitBeforeLeaving { get; set; }

        public bool PathDrawEnabled   { get; set; } = false;
        public int  PathDrawStepCount { get; set; } = 5;

        public bool DisableRenderWhileActive
        {
            get;
            set
            {
                field = value;
                if (!value)
                    RenderDisableManager.RemoveRequest();
                else if (Plugin.States != PluginState.None)
                    RenderDisableManager.PlaceRequest();
            }
        } = false;

        public bool OverridePartyValidation { get; set; } = false;
        public bool UsingAlternativeRotationPlugin { get; set; } = false;
        public bool UsingAlternativeMovementPlugin { get; set; } = false;
        public bool UsingAlternativeBossPlugin { get; set; } = false;

        public bool TreatUnsyncAsW2W { get; set; } = true;
        public JobWithRole W2WJobs { get; set; } = JobWithRole.Tanks;

        public bool IsW2W(Job? job = null, bool? unsync = null)
        {
            job ??= PlayerHelper.GetJob();

            if (this.W2WJobs.HasJob(job.Value))
                return true;
            
            unsync ??= this.config.Meta.Unsynced && this.config.Meta.DutyModeEnum.EqualsAny(DutyMode.Raid, DutyMode.Regular, DutyMode.Trial);

            return MultiboxUtility.Config.MultiBox || unsync.Value && this.TreatUnsyncAsW2W;
        }

        public bool LevelingListExperimentalEntries
        {
            get;
            set
            {
                if (field != value)
                    LevelingHelper.ResetLevelingDuties();
                field = value;
            }
        } = false;
    }

    public LoopConfig Loop { get; set; }

    [JsonObject(MemberSerialization.OptOut)]
    public class LoopConfig
    {
        [JsonIgnore]
        private readonly ConfigurationProfileV2 config;

        public LoopConfig(ConfigurationProfileV2 config)
        {
            this.config      = config;
            this.Pre         = new PreLoopConfig(this);
            this.Between     = new BetweenLoopConfig(this);
            this.Termination = new TerminationConfig(this);
        }

        public PreLoopConfig Pre { get; set; }

        [JsonObject(MemberSerialization.OptOut)]
        public class PreLoopConfig(LoopConfig config)
        {
            [JsonIgnore]
            private readonly LoopConfig config = config;

            public bool Enabled { get; set; } = true;

            public LoopActions Actions { get; set; } = [ 
                new ExecuteCommandsLoopActionConfig { Enabled = false }, 
                new PlaylistPreLoopActionConfig(),
                new ConsumeItemsLoopActionConfig { Enabled = false },
                new AutoEquipLoopActionConfig { Enabled = false },
                new RepairLoopActionConfig { Enabled = false },
                new RetireLoopActionConfig { Enabled = false }
            ];
        }
        
        public BetweenLoopConfig Between { get; set; }

        [JsonObject(MemberSerialization.OptOut)]
        public class BetweenLoopConfig(LoopConfig config)
        {
            [JsonIgnore]
            private readonly LoopConfig config = config;

            public bool Enabled         { get; set; } = true;
            public bool ExecuteLastLoop { get; set; } = false;

            public LoopActions Actions { get; set; } =
            [
                new WaitLoopActionConfig { Enabled                  = false },
                new ExecuteCommandsLoopActionConfig { Enabled       = false },
                new CofferOpenLoopActionConfig { Enabled            = false },
                new AutoRetainerLoopActionConfig { Enabled          = false },
                new AutoRetainerMultiModeLoopActionConfig { Enabled = false },
                new PlaylistSwitchLoopActionConfig(),
                new AutoEquipLoopActionConfig { Enabled       = false },
                new ExtractLoopActionConfig { Enabled         = false },
                new DesynthLoopActionConfig { Enabled         = false },
                new GCTurnInLoopActionConfig { Enabled        = false },
                new TripleTriadUseLoopActionConfig { Enabled  = false },
                new TripleTriadSellLoopActionConfig { Enabled = false },
                new DiscardItemsLoopActionConfig { Enabled    = false },
                new ConsumeItemsLoopActionConfig { Enabled    = false }
            ];
        }

        public TerminationConfig Termination { get; set; }

        [JsonObject(MemberSerialization.OptOut)]
        public class TerminationConfig(LoopConfig config)
        {
            private readonly LoopConfig config = config;

            public bool                                        Enabled      { get; set; } = true;

            public bool                                        StopLevel                     { get; set; }
            public int                                         StopLevelInt                  { get; set; } = 1;
            public bool                                        StopNoRestedXP                { get; set; }
            public bool                                        StopItemQty                   { get; set; }
            public bool                                        StopItemAll                   { get; set; }
            public Dictionary<uint, KeyValuePair<string, int>> StopItemQtyItemDictionary     { get; set; } = [];
            public int                                         StopItemQtyInt                { get; set; } = 1;
            public bool                                        StopWhenDutyGathered          { get; set; }
            public bool                                        TerminationBLUSpellsEnabled   { get; set; }
            public List<uint>                                  TerminationBLUSpells          { get; set; } = [];
            public bool                                        TerminationBLUSpellsAll       { get; set; }
            public bool                                        TerminationInventoryFree      { get; set; }
            public int                                         TerminationInventoryFreeSlots { get; set; }
            public bool                                        TerminationiLvl               { get; set; }
            public int                                         TerminationiLvlInt            { get; set; }

            public LoopActions Actions { get; set; } =
            [
                new ExecuteCommandsLoopActionConfig { Enabled = false },
                new PlaySoundLoopActionConfig { Enabled       = false }
            ];

            public TerminationMode                 TerminationMethodEnum     { get; set; } = TerminationMode.Do_Nothing;
            public bool                            TerminationKeepActive     { get; set; } = true;
        }
    }

    public class LoopActions : List<LoopActionConfig>
    {
        private float imguiListX = 400;

        /// <summary>Index of the entry being dragged during the last frame, -1 if none.</summary>
        private int dragSourceIndex = -1;

        public LoopActionConfig<T>? FindConfig<T>() where T : LoopActionConfig<T>, new() =>  
            (LoopActionConfig<T>?) this.FirstOrDefault(lac => lac is T);

        public bool RunConfig<T>(bool queue = true) where T : LoopActionConfig<T>, new()
        {
            LoopActionConfig? config = this.FindConfig<T>();
            if (config == null)
                return false;

            config.Run(ref queue);
            return queue;
        }

        /// <summary>
        /// Toggles the settings of all entries at once: collapses everything if any entry is open, expands everything otherwise.
        /// </summary>
        public void DrawToggleAllButton(string id)
        {
            bool anyOpen = this.Any(lac => lac.IsConfigOpen);
            string caret = (anyOpen ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight).ToIconString();

            using (ImRaii.PushColor(ImGuiCol.Button, Vector4.Zero))
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button($"{caret}{caret}###toggleAllLoopActions{id}"))
                    this.ForEach(lac => lac.SetConfigOpen(!anyOpen));
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get(anyOpen ? "LoopActions.CollapseAll" : "LoopActions.ExpandAll"));
        }

        public void OnGui(string id, LoopActionCategory category = LoopActionCategory.All, bool startOpened = true)
        {
            if (!ImGui.BeginListBox($"##LoopActions_{id}", new Vector2(ImGui.GetContentRegionAvail().X, this.imguiListX)))
                return;

            ImGui.PushItemWidth(150f.Scale());

            // ImGui payload types are limited to 32 characters, so the list id is hashed to keep it short
            string dragDropId = $"ADLoopAction{(uint)id.GetHashCode():X8}";
            int    moveFrom   = -1;
            int    moveTo     = -1;
            int    dragging   = -1;

            for (int index = 0; index < this.Count; index++)
            {
                using ImRaii.IdDisposable _ = ImRaii.PushId($"{id}_{index}");

                LoopActionConfig actionConfig = this[index];

                ImGui.BeginGroup();

                using (ImRaii.PushFont(UiBuilder.IconFont))
                    ImGui.Button($"{FontAwesomeIcon.GripVertical.ToIconString()}###dragLoopAction{id}_{index}", new Vector2(28f.Scale(), 0));
                if (ImGui.IsItemHovered())
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

                // Pushed around the whole source so the preview tooltip window itself is translucent too
                using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.75f))
                {
                    if (ImGui.BeginDragDropSource())
                    {
                        ImGuiDragDrop.SetDragDropPayload(dragDropId, index.ToString());
                        actionConfig.DrawDragPreview();
                        ImGui.EndDragDropSource();
                        dragging = index;
                    }
                }

                ImGui.SameLine();

                bool deleted = false;
                if (ImGui.GetIO().KeyCtrl)
                    using (ImRaii.PushColor(ImGuiCol.Button, ImGuiHelper.AccentRed with { W = 0.15f.Scale() }))
                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiHelper.AccentRed))
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        if (ImGui.Button($"{FontAwesomeIcon.TrashAlt.ToIconString()}###deleteDataLoopAction{id}_{index}", new Vector2(28f.Scale(), 0)))
                            deleted = true;

                        ImGui.SameLine();
                    }

                if (deleted)
                {
                    ImGui.EndGroup();
                    this.Remove(actionConfig);
                    index--;
                    Save();
                    continue;
                }

                actionConfig.OnGUI(startOpened && AutoDuty.Configuration.Meta.LoopActionsOpenByDefault);

                ImGui.EndGroup();

                Vector2       rowMin   = ImGui.GetItemRectMin();
                Vector2       rowMax   = ImGui.GetItemRectMax();
                ImDrawListPtr drawList = ImGui.GetWindowDrawList();

                // Dim the entry that is being dragged
                if (index == dragging || index == this.dragSourceIndex)
                    drawList.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.5f)));

                if (ImGui.BeginDragDropTarget())
                {
                    ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(dragDropId, ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                    int             from    = this.dragSourceIndex;

                    if (!payload.IsNull && from >= 0 && from != index)
                    {
                        // Line where the entry will be inserted: below the target when moving down, above it when moving up
                        float y = from < index ? rowMax.Y : rowMin.Y;
                        drawList.AddLine(new Vector2(rowMin.X, y), new Vector2(rowMax.X, y), ImGui.GetColorU32(ImGuiCol.DragDropTarget), 2f.Scale());

                        if (payload.IsDelivery())
                        {
                            moveFrom = from;
                            moveTo   = index;
                        }
                    }
                    ImGui.EndDragDropTarget();
                }
            }

            this.dragSourceIndex = dragging;

            if (moveFrom >= 0 && moveFrom < this.Count && moveFrom != moveTo)
            {
                LoopActionConfig moved = this[moveFrom];
                this.RemoveAt(moveFrom);
                this.Insert(moveTo, moved);
                Save();
            }

            ImGui.PopItemWidth();

            this.imguiListX = ImGui.GetCursorPosY();
            ImGui.EndListBox();

            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button($"{FontAwesomeIcon.Plus.ToIconString()}###addLoopAction{id}", new Vector2(28f.Scale(), 0)))
                    ImGui.OpenPopup($"##LoopActions{id}ContextMenu");
            }

            ImGui.SameLine();
            ImGui.TextColored(ImGuiHelper.VersionColor, Loc.Get("LoopActions.Hint"));

            if (ImGui.IsPopupOpen($"##LoopActions{id}ContextMenu"))
                if (ImGui.BeginPopup($"##LoopActions{id}ContextMenu", ImGuiWindowFlags.AlwaysAutoResize))
                {
                    HashSet<Type> shownTypes = [];

                    void DrawLoopActions(List<Tuple<Type, string>> tuples)
                    {
                        foreach ((Type type, string nameKey) in tuples)
                        {
                            if (!shownTypes.Add(type))
                                continue;

                            string name = nameKey.Length == 0 ? string.Empty : Loc.Get(nameKey);

                            if (ImGui.Selectable($"{name}##{id}_{type}_{nameKey}") && Activator.CreateInstance(type) is LoopActionConfig actionConfig)
                            {
                                this.Add(actionConfig);
                                Save();
                            }
                        }
                    }

                    DrawLoopActions(LoopActionConfig.actionCategories[category]);

                    if (category != LoopActionCategory.All)
                    {
                        LoopActionCategory[] actionCategories = Enum.GetValues<LoopActionCategory>();

                        foreach (LoopActionCategory actionCategory in actionCategories)
                        {
                            if (actionCategory == category || actionCategory == LoopActionCategory.All)
                                continue;

                            ImGui.Separator();
                            DrawLoopActions(LoopActionConfig.actionCategories[actionCategory]);
                        }
                    }

                    ImGui.EndPopup();
                }
        }
    }

    public TrustMemberName?[] SelectedTrustMembers { get; set; } = new TrustMemberName?[3];

    public static void Save() => 
        ConfigurationMain.Save();
}