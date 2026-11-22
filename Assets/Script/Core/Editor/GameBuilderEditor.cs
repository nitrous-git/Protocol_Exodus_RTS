using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(GameBuilder)), CanEditMultipleObjects]
internal sealed class GameBuilderEditor : Editor
{
    private const string StylePath = "Assets/Script/Core/Editor/GameBuilderEditor.uss";

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.AddToClassList("game-builder-inspector");
        var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
        if (style != null)
            root.styleSheets.Add(style);

        var matchMode = serializedObject.FindProperty("matchProfile");
        var strategy = serializedObject.FindProperty("aiStrategy");
        var profile = strategy.FindPropertyRelative(nameof(AIStrategySettings.mediumProfile));
        var waves = serializedObject.FindProperty("aiWaves");
        var economy = serializedObject.FindProperty("aiEconomy");
        var strategyEnabled = strategy.FindPropertyRelative(nameof(AIStrategySettings.enableStrategyAI));
        var openingEnabled = strategy.FindPropertyRelative(nameof(AIStrategySettings.enableJavaInspiredOpening));
        var wavesEnabled = waves.FindPropertyRelative(nameof(AIWaveSettings.automaticWaves));
        var economyEnabled = economy.FindPropertyRelative(nameof(AIEconomySettings.enableEconomyAI));
        int standardIndex = Array.IndexOf(matchMode.enumNames, "StandardMatch");

        var match = Section(root, "Match Setup");
        Field(match, matchMode, "Match Mode");
        var effectiveMode = Message(match);
        var standardSetup = Container(match);
        Field(standardSetup, serializedObject.FindProperty("standardStartingMinerals"), "Starting Minerals");
        Field(standardSetup, serializedObject.FindProperty("standardStartingSupply"), "Starting Supply");
        var sandboxSetup = Container(match);
        Message(sandboxSetup, "Starting minerals, gas, and supply come from the faction definition.");
        Field(sandboxSetup, serializedObject.FindProperty("startingCombatCount"), "Starting Combat Units");
        Field(sandboxSetup, serializedObject.FindProperty("startingUnitSpacing"), "Starting Unit Spacing");
        Field(sandboxSetup, strategyEnabled, "Enable A4 Strategy");

        var medium = Section(root, "AI Strategy — Medium");
        DrawMedium(medium, profile, out var attackWarning, out var armyWarning, out var defenseWarning);
        var opening = Section(medium, "Shared Opening", false);
        DrawOpening(opening, strategy, openingEnabled, out var openingTimings);
        var openingWarning = Message(medium, type: HelpBoxMessageType.Warning);

        var execution = Section(root, "Advanced AI Execution", false);
        var squad = Section(execution, "A2 — Squad Execution");
        var missionTests = DrawSquadExecution(squad, waves);
        var production = Section(execution, "A3 — Economy Execution");
        var productionSettings = Container(production);
        DrawEconomyExecution(productionSettings, economy);
        var evaluation = Section(execution, "A4 — Strategy Evaluation", false);
        Field(evaluation, strategy.FindPropertyRelative(nameof(AIStrategySettings.decisionInterval)), "Strategy Decision Interval",
            "Seconds between strategy evaluations for army goals, threats, and assault decisions.");

        var standalone = Section(execution, "Standalone Development Controls");
        var automaticWaveSettings = DrawStandaloneWaves(standalone, waves, wavesEnabled);
        var standaloneEconomyGoals = DrawStandaloneEconomy(standalone, economy, economyEnabled);
        DrawReferences(root);

        void Refresh(SerializedObject _)
        {
            bool sameMode = !matchMode.hasMultipleDifferentValues;
            bool standard = sameMode && matchMode.enumValueIndex == standardIndex;
            bool sandbox = sameMode && !standard;
            bool strategyMixed = sandbox && strategyEnabled.hasMultipleDifferentValues;
            bool useStrategy = standard || (sandbox && !strategyMixed && strategyEnabled.boolValue);
            bool useStandalone = sandbox && !strategyMixed && !strategyEnabled.boolValue;

            Show(standardSetup, standard);
            Show(sandboxSetup, sandbox);
            Show(medium, useStrategy);
            Show(evaluation, useStrategy);
            Show(standalone, useStandalone);
            openingTimings.SetEnabled(openingEnabled.boolValue || openingEnabled.hasMultipleDifferentValues);
            automaticWaveSettings.SetEnabled(wavesEnabled.boolValue || wavesEnabled.hasMultipleDifferentValues);
            standaloneEconomyGoals.SetEnabled(economyEnabled.boolValue || economyEnabled.hasMultipleDifferentValues);
            productionSettings.SetEnabled(!useStandalone || economyEnabled.boolValue || economyEnabled.hasMultipleDifferentValues);
            missionTests.text = useStandalone ? "A2 — Standalone & Manual Mission Limits" : "A2 — Manual Mission Test Limits";

            if (!sameMode || strategyMixed)
                effectiveMode.text = "The selection has different AI configurations. Select GameBuilders with matching modes and A4 settings to edit mode-specific controls together.";
            else if (useStrategy)
                effectiveMode.text = standard
                    ? "Standard Match always enables A4. Medium sets strategic goals and assault timing. A2 executes missions and A3 executes production. Standalone waves and economy goals are inactive."
                    : "A4 is enabled. Medium sets strategic goals and assault timing. A2 executes missions and A3 executes production. Standalone waves and economy goals are inactive.";
            else
                effectiveMode.text = "A4 is disabled. Automatic waves and AI production use the Standalone Development Controls under Advanced AI Execution.";

            if (useStrategy)
                RefreshWarnings(profile, strategy, openingEnabled, attackWarning, armyWarning, defenseWarning, openingWarning);
        }

        Refresh(serializedObject);
        root.TrackSerializedObjectValue(serializedObject, Refresh);
        return root;
    }

    private static void DrawMedium(Foldout medium, SerializedProperty profile, out HelpBox attackWarning,
        out HelpBox armyWarning, out HelpBox defenseWarning)
    {
        var attack = Section(medium, "Attack & Waves");
        Field(attack, profile.FindPropertyRelative(nameof(AIStrategyProfile.minimumAttackForce)), "Minimum Attack Force",
            "Minimum eligible units after retaining home defenders. Applies to ordinary assaults after the preparation period.");
        Field(attack, profile.FindPropertyRelative(nameof(AIStrategyProfile.desiredAttackForce)), "Desired Attack Force",
            "Preferred number of attackers while preparing an ordinary assault. Also helps determine army growth.");
        Field(attack, profile.FindPropertyRelative(nameof(AIStrategyProfile.maximumAttackForce)), "Maximum Attack Force",
            "Upper limit on units assigned to a strategic assault. Opening assaults may use smaller limits.");
        Field(attack, profile.FindPropertyRelative(nameof(AIStrategyProfile.forcePreparationDuration)), "Force Preparation Duration",
            "Additional seconds to wait for the desired force before allowing a smaller qualifying assault.");
        Field(attack, profile.FindPropertyRelative(nameof(AIStrategyProfile.attackCooldownMin)), "Attack Cooldown Min",
            "Lower bound in seconds for the post-assault cooldown outside the opening.");
        Field(attack, profile.FindPropertyRelative(nameof(AIStrategyProfile.attackCooldownMax)), "Attack Cooldown Max",
            "Upper bound in seconds for the post-assault cooldown outside the opening.");
        attackWarning = Message(attack, type: HelpBoxMessageType.Warning);

        var growth = Section(medium, "Economy & Army");
        Field(growth, profile.FindPropertyRelative(nameof(AIStrategyProfile.developedWorkerGoal)), "Developed Worker Goal",
            "Worker goal once the economy is established. Strategy keeps at least the initial worker target.");
        Field(growth, profile.FindPropertyRelative(nameof(AIStrategyProfile.maximumArmyTarget)), "Maximum Army Target",
            "Caps the army size requested by strategy. The requested goal varies with the opening, defense, and army readiness.");
        Field(growth, profile.FindPropertyRelative(nameof(AIStrategyProfile.maximumBarracksTarget)), "Maximum Barracks Target",
            "Caps the requested Barracks count. A second Barracks requires an established economy and enough military strength.");
        Field(growth, profile.FindPropertyRelative(nameof(AIStrategyProfile.infantryToMechRatio)), "Infantry-to-Mech Ratio",
            "Preferred number of infantry units per mech for army production.");
        Field(growth, profile.FindPropertyRelative(nameof(AIStrategyProfile.barracksExpansionMineralBuffer)), "Barracks Expansion Mineral Buffer",
            "Extra minerals required to unlock a second Barracks, beyond its cost and any headquarters reserve.");
        Field(growth, profile.FindPropertyRelative(nameof(AIStrategyProfile.supplyBuffer)), "Supply Buffer",
            "Extra free supply the AI aims to maintain beyond the next unit's supply cost.");
        armyWarning = Message(growth, type: HelpBoxMessageType.Warning);

        var defense = Section(medium, "Defense & Recovery");
        Field(defense, profile.FindPropertyRelative(nameof(AIStrategyProfile.homeDefenseReserve)), "Home Defense Reserve",
            "Units retained for home defense when selecting attackers.");
        Field(defense, profile.FindPropertyRelative(nameof(AIStrategyProfile.maximumDefenders)), "Maximum Defenders",
            "Caps the requested response to threats. The effective cap is at least the home defense reserve.");
        Field(defense, profile.FindPropertyRelative(nameof(AIStrategyProfile.defenseThreatRadius)), "Defense Threat Radius",
            "World-space distance around base assets used to detect threats and establish the defense area.");
        Field(defense, profile.FindPropertyRelative(nameof(AIStrategyProfile.defenseStandDownDelay)), "Defense Stand-Down Delay",
            "Seconds to maintain the elevated defense response after the last observed threat.");
        Field(defense, profile.FindPropertyRelative(nameof(AIStrategyProfile.recoveryDuration)), "Recovery Duration",
            "Seconds the strategy may hold attacks after losses outside the opening.");
        defenseWarning = Message(defense, type: HelpBoxMessageType.Warning);
    }

    private static void DrawOpening(Foldout opening, SerializedProperty strategy, SerializedProperty enabled, out VisualElement timings)
    {
        Field(opening, enabled, "Enable Java-Inspired Opening");
        Field(opening, strategy.FindPropertyRelative(nameof(AIStrategySettings.initialWorkerGoal)), "Initial Worker Target",
            "Sets A4's early worker goal and the starting worker count for both factions in Standard Match, including when the Java-inspired opening is disabled.");
        Field(opening, strategy.FindPropertyRelative(nameof(AIStrategySettings.baseBuildingGrace)), "Base-Building Grace Period",
            "Earliest match time in seconds at which strategy may launch an assault, including outside the Java-inspired opening.");
        Field(opening, strategy.FindPropertyRelative(nameof(AIStrategySettings.openingRetryWindow)), "Opening Retry Window",
            "Seconds an opening assault remains available, up to the next opportunity. Also allows Barracks growth with fewer than the initial worker target after a delay.");
        timings = Container(opening);
        Field(timings, strategy.FindPropertyRelative(nameof(AIStrategySettings.barracksOpportunity)), "Barracks Opportunity",
            "Earliest match time in seconds at which the opening can request a Barracks.");
        Field(timings, strategy.FindPropertyRelative(nameof(AIStrategySettings.openingProbeTime)), "Probe Time",
            "Earliest match time in seconds for the probe, subject to the base-building grace period.");
        Field(timings, strategy.FindPropertyRelative(nameof(AIStrategySettings.firstAssaultTime)), "First Assault Time",
            "Earliest match time in seconds for the first organized assault.");
        Field(timings, strategy.FindPropertyRelative(nameof(AIStrategySettings.reinforcementTime)), "Reinforcement Time",
            "Match time in seconds when the opening raises army production goals for the larger assault.");
        Field(timings, strategy.FindPropertyRelative(nameof(AIStrategySettings.secondAssaultTime)), "Larger Assault Time",
            "Earliest match time in seconds for the larger opening assault.");
        Field(timings, strategy.FindPropertyRelative(nameof(AIStrategySettings.probeDuration)), "Probe Duration",
            "Maximum duration of the probe in seconds.");
        Field(timings, strategy.FindPropertyRelative(nameof(AIStrategySettings.openingAttackCooldown)), "Opening Attack Cooldown",
            "Seconds between an assault ending and the next possible dispatch during the opening.");
        Field(timings, strategy.FindPropertyRelative(nameof(AIStrategySettings.openingRecoveryDuration)), "Opening Recovery Duration",
            "Seconds the strategy may hold attacks after losses during the opening.");
    }

    private static Foldout DrawSquadExecution(Foldout squad, SerializedProperty waves)
    {
        Field(squad, waves.FindPropertyRelative(nameof(AIWaveSettings.stagingDistance)), "Staging Distance",
            "Preferred staging distance from home in world units.");
        Field(squad, waves.FindPropertyRelative(nameof(AIWaveSettings.assemblyReadiness)), "Assembly Readiness",
            "Fraction of surviving squad members that must reach staging before the squad advances.");
        Field(squad, waves.FindPropertyRelative(nameof(AIWaveSettings.assemblyTimeout)), "Assembly Timeout",
            "Seconds allowed for the squad to assemble at staging.");
        Field(squad, waves.FindPropertyRelative(nameof(AIWaveSettings.withdrawalThreshold)), "Withdrawal Threshold");
        var tests = Section(squad, "A2 — Manual Mission Test Limits", false);
        Message(tests, "These limits apply to the manual AI Missions actions. Standalone automatic waves also use the assault limits when enabled.");
        Field(tests, waves.FindPropertyRelative(nameof(AIWaveSettings.minimumParticipants)), "Minimum Assault Participants");
        Field(tests, waves.FindPropertyRelative(nameof(AIWaveSettings.maximumParticipants)), "Maximum Assault Participants");
        Field(tests, waves.FindPropertyRelative(nameof(AIWaveSettings.homeDefenders)), "Home Defenders",
            "Reserve for manual and standalone assaults. Also sets the requested squad size for the manual defense test, with a minimum of one.");
        Field(tests, waves.FindPropertyRelative(nameof(AIWaveSettings.defenseGuardRadius)), "Defense Guard Radius",
            "World-space guard radius for Assign Test Base Defense. A4 defense uses Medium's Defense Threat Radius.");
        return tests;
    }

    private static void DrawEconomyExecution(VisualElement production, SerializedProperty economy)
    {
        Field(production, economy.FindPropertyRelative(nameof(AIEconomySettings.decisionInterval)), "Decision Interval",
            "Seconds between production and construction decisions.");
        Field(production, economy.FindPropertyRelative(nameof(AIEconomySettings.maximumQueuedOrdersPerProducer)), "Production Queue Limit",
            "Maximum queued orders per producing building.");
        Field(production, economy.FindPropertyRelative(nameof(AIEconomySettings.buildingSearchRadius)), "Building Search Radius");
        Field(production, economy.FindPropertyRelative(nameof(AIEconomySettings.buildingSpacing)), "Building Spacing");
        Field(production, economy.FindPropertyRelative(nameof(AIEconomySettings.failedActionRetryInterval)), "Failed-Action Retry Interval",
            "Seconds before retrying a failed economy action.");
    }

    private static VisualElement DrawStandaloneWaves(Foldout standalone, SerializedProperty waves, SerializedProperty enabled)
    {
        var section = Section(standalone, "A2 — Automatic Waves");
        Field(section, enabled, "Enable Automatic Waves");
        var timing = Container(section);
        Field(timing, waves.FindPropertyRelative(nameof(AIWaveSettings.initialDelay)), "Initial Wave Delay");
        Field(timing, waves.FindPropertyRelative(nameof(AIWaveSettings.waveCooldown)), "Wave Cooldown");
        Message(section, "Set wave size and home defenders in A2 — Squad Execution under Standalone & Manual Mission Limits.");
        return timing;
    }

    private static VisualElement DrawStandaloneEconomy(Foldout standalone, SerializedProperty economy, SerializedProperty enabled)
    {
        var section = Section(standalone, "A3 — Standalone Economy");
        Field(section, enabled, "Enable Standalone Economy");
        var goals = Container(section);
        Field(goals, economy.FindPropertyRelative(nameof(AIEconomySettings.targetWorkerCount)), "Worker Target");
        Field(goals, economy.FindPropertyRelative(nameof(AIEconomySettings.targetGasWorkerCount)), "Gas Worker Target",
            "Workers assigned to gas, up to the total worker target.");
        Field(goals, economy.FindPropertyRelative(nameof(AIEconomySettings.targetBarracksCount)), "Barracks Target");
        Field(goals, economy.FindPropertyRelative(nameof(AIEconomySettings.targetArmySize)), "Army Target");
        Field(goals, economy.FindPropertyRelative(nameof(AIEconomySettings.infantryToMechRatio)), "Infantry-to-Mech Ratio");
        Field(goals, economy.FindPropertyRelative(nameof(AIEconomySettings.supplyBuffer)), "Supply Buffer");
        return goals;
    }

    private void DrawReferences(VisualElement root)
    {
        var setup = Section(root, "Scene References & UI", false);
        Field(setup, serializedObject.FindProperty("m_Script"), "Script").SetEnabled(false);
        Field(setup, serializedObject.FindProperty("gameLoop"), "Game Loop");
        Field(setup, serializedObject.FindProperty("matchWorld"), "Match World");
        Field(setup, serializedObject.FindProperty("matchUI"), "Match UI");
        Field(setup, serializedObject.FindProperty("gameLogCapacity"), "Game Log Capacity");
        Field(setup, serializedObject.FindProperty("gameLogDisplayDuration"), "Game Log Display Duration");
        Field(setup, serializedObject.FindProperty("FactionTerran_Definition"), "Terran Faction Definition");
        Field(setup, serializedObject.FindProperty("FactionSpecter_Definition"), "Specter Faction Definition");
    }

    private static void RefreshWarnings(SerializedProperty profile, SerializedProperty strategy, SerializedProperty openingEnabled,
        HelpBox attack, HelpBox army, HelpBox defense, HelpBox opening)
    {
        var minimum = profile.FindPropertyRelative(nameof(AIStrategyProfile.minimumAttackForce));
        var desired = profile.FindPropertyRelative(nameof(AIStrategyProfile.desiredAttackForce));
        var maximum = profile.FindPropertyRelative(nameof(AIStrategyProfile.maximumAttackForce));
        var cooldownMin = profile.FindPropertyRelative(nameof(AIStrategyProfile.attackCooldownMin));
        var cooldownMax = profile.FindPropertyRelative(nameof(AIStrategyProfile.attackCooldownMax));
        SetWarning(attack,
            Above(minimum, desired) ? "Minimum Attack Force exceeds Desired Attack Force. Runtime raises the effective desired force to at least the effective minimum." : null,
            Above(desired, maximum) ? "Desired Attack Force exceeds Maximum Attack Force. Runtime caps the effective desired force at the maximum." : null,
            Above(minimum, maximum) ? "Minimum Attack Force exceeds Maximum Attack Force. Runtime caps the effective minimum at the maximum." : null,
            Above(cooldownMin, cooldownMax) ? "Attack Cooldown Min exceeds Attack Cooldown Max. Runtime uses the minimum for both bounds." : null);

        var reserve = profile.FindPropertyRelative(nameof(AIStrategyProfile.homeDefenseReserve));
        var armyCap = profile.FindPropertyRelative(nameof(AIStrategyProfile.maximumArmyTarget));
        bool armyTooSmall = !desired.hasMultipleDifferentValues && !reserve.hasMultipleDifferentValues &&
            !armyCap.hasMultipleDifferentValues && (long)desired.intValue + reserve.intValue > armyCap.intValue;
        SetWarning(army, armyTooSmall ? "Maximum Army Target is below Desired Attack Force plus Home Defense Reserve. The capped production goal may leave too few units to prepare a full assault." : null);
        SetWarning(defense, Above(reserve, profile.FindPropertyRelative(nameof(AIStrategyProfile.maximumDefenders)))
            ? "Home Defense Reserve exceeds Maximum Defenders. Runtime raises the effective defender cap to the reserve." : null);

        if (!openingEnabled.boolValue || openingEnabled.hasMultipleDifferentValues)
        {
            Show(opening, false);
            return;
        }

        var grace = strategy.FindPropertyRelative(nameof(AIStrategySettings.baseBuildingGrace));
        var barracks = strategy.FindPropertyRelative(nameof(AIStrategySettings.barracksOpportunity));
        var probe = strategy.FindPropertyRelative(nameof(AIStrategySettings.openingProbeTime));
        var first = strategy.FindPropertyRelative(nameof(AIStrategySettings.firstAssaultTime));
        var reinforcement = strategy.FindPropertyRelative(nameof(AIStrategySettings.reinforcementTime));
        var larger = strategy.FindPropertyRelative(nameof(AIStrategySettings.secondAssaultTime));
        SetWarning(opening,
            Above(grace, probe) ? "Probe Time is before the grace period ends. Runtime delays the probe opportunity." : null,
            Above(barracks, probe) ? "Barracks Opportunity is after Probe Time. The probe requires an operational Barracks." : null,
            Above(probe, first, includeEqual: true) || Above(first, larger, includeEqual: true)
                ? "Schedule the opening probe, first assault, and larger assault at successively later times to give each its own opportunity." : null,
            Above(first, reinforcement) || Above(reinforcement, larger)
                ? "Reinforcement Time should fall between the first and larger assaults. Runtime constrains that production milestone to the effective assault times." : null);
    }

    private static bool Above(SerializedProperty left, SerializedProperty right, bool includeEqual = false)
    {
        if (left.hasMultipleDifferentValues || right.hasMultipleDifferentValues)
            return false;

        double first = left.propertyType == SerializedPropertyType.Integer ? (double)left.intValue : left.floatValue;
        double second = right.propertyType == SerializedPropertyType.Integer ? (double)right.intValue : right.floatValue;
        return includeEqual ? first >= second : first > second;
    }

    private static void SetWarning(HelpBox box, params string[] messages)
    {
        box.text = string.Join("\n", Array.FindAll(messages, message => message != null));
        Show(box, box.text.Length > 0);
    }

    private static Foldout Section(VisualElement parent, string title, bool expanded = true)
    {
        var foldout = new Foldout { text = title, value = expanded, viewDataKey = title };
        parent.Add(foldout);
        return foldout;
    }

    private static VisualElement Container(VisualElement parent)
    {
        var container = new VisualElement();
        parent.Add(container);
        return container;
    }

    private static PropertyField Field(VisualElement parent, SerializedProperty property, string label, string tooltip = null)
    {
        var field = new PropertyField(property, label) { tooltip = tooltip ?? property.tooltip };
        parent.Add(field);
        return field;
    }

    private static HelpBox Message(VisualElement parent, string text = "", HelpBoxMessageType type = HelpBoxMessageType.Info)
    {
        var box = new HelpBox(text, type);
        parent.Add(box);
        return box;
    }

    private static void Show(VisualElement element, bool visible)
    {
        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
