using System.Collections.Generic;
using System.Linq;
using BepInEx;
//using R2API;
//using R2API.Utils;
using RoR2;
using UnityEngine;
using CreditsController = On.RoR2.CreditsController;
using GameOverController = On.RoR2.GameOverController;
using PreGameController = On.RoR2.PreGameController;
using VoteController = On.RoR2.VoteController;

namespace ReadyConfig
{
    //[BepInDependency(R2API.R2API.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    //[R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI))]
    public class ExamplePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Maci";
        public const string PluginName = "ReadyConfig";
        public const string PluginVersion = "1.0.0";

        public static BepInEx.Configuration.ConfigEntry<bool> EnableLobbyChanges { get; set; }
        public static BepInEx.Configuration.ConfigEntry<bool> LobbyVotesRequireHostReady { get; set; }
        public static BepInEx.Configuration.ConfigEntry<bool> LobbyStartOnHostReady { get; set; }
        public static BepInEx.Configuration.ConfigEntry<bool> LobbyStartRequiresAllReady { get; set; }
        public static BepInEx.Configuration.ConfigEntry<float> LobbyCountdownTime { get; set; }

        public static BepInEx.Configuration.ConfigEntry<bool> EnableGameOverChanges { get; set; }
        public static BepInEx.Configuration.ConfigEntry<bool> GameOverRequiresAllReady { get; set; }
        public static BepInEx.Configuration.ConfigEntry<float> GameOverCountdownTime { get; set; }

        public static BepInEx.Configuration.ConfigEntry<bool> EnableCreditsChanges { get; set; }
        public static BepInEx.Configuration.ConfigEntry<bool> CreditsCountdownOnAnyReady { get; set; }
        public static BepInEx.Configuration.ConfigEntry<float> CreditsCountdownTime { get; set; }

        private static RoR2.VoteController PreGameVoteController;
        private static RoR2.VoteController GameOverVoteController;
        private static RoR2.VoteController CreditsVoteController;


        public void Awake()
        {
            Log.Init(Logger);

            if (UnityEngine.Application.isBatchMode)
            {
                Log.LogWarning("Running on dedicated server, " + nameof(Awake) + " cancelled");
                return;
            }

            EnableGameOverChanges = Config.Bind<bool>("GameOver", "EnableGameOverChanges", false,
                "Enable or Disable all features related to the GameOver screen");
            GameOverRequiresAllReady = Config.Bind<bool>("GameOver", "GameOverRequiresAllReady", true,
                "Instead of starting the countdown when anyone has voted to skip, requires all Players to press vote");
            GameOverCountdownTime = Config.Bind<float>("GameOver", "GameOverCountdownTime", 15f,
                "The countdown until skipped when not using GameOverRequiresAllReady, in seconds");

            EnableCreditsChanges = Config.Bind<bool>("Credits", "EnableCreditsChanges", true,
                "Enable or Disable all features related to the Credits screen");
            CreditsCountdownOnAnyReady = Config.Bind<bool>("Credits", "CreditsCountdownOnAnyReady", true,
                "Instead of requiring everybody to vote, starts a countdown when at least one Player votes to skip");
            CreditsCountdownTime = Config.Bind<float>("Credits", "CreditsCountdownTime", 15f,
                "The countdown until skipped when using CreditsCountdownOnAnyReady, in seconds");

            EnableLobbyChanges = Config.Bind<bool>("Lobby", "EnableLobbyChanges", true,
                "Enable or Disable all features related to the Lobby");
            LobbyVotesRequireHostReady = Config.Bind<bool>("Lobby", "LobbyVotesRequireHostReady", false,
                "Players can't click Ready unless Host is Ready, disabled on dedicated servers");
            LobbyStartOnHostReady = Config.Bind<bool>("Lobby", "LobbyStartOnHostReady", false,
                "Makes everyone Ready when Host is Ready and starts the game, disabled on dedicated servers");
            LobbyStartRequiresAllReady = Config.Bind<bool>("Lobby", "LobbyStartRequiresAllReady", true,
                "Instead of starting the countdown when anyone has presses Ready, requires all Players to press Ready");
            LobbyCountdownTime = Config.Bind<float>("Lobby", "LobbyCountdownTime", 60f,
                "The countdown until the game is started when not using LobbyStartRequiresAllReady, in seconds");

            Log.LogDebug("Config:");
            foreach (var c in Config)
            {
                Log.LogDebug("(" + c.Key.Section + ") " + c.Key.Key + ": " + c.Value.BoxedValue);
            }

            #if DEBUG // Makes it possible to join the same lobby multiple times
                On.RoR2.Networking.NetworkManagerSystemSteam.OnClientConnect += (s, u, t) => { };
            #endif

            if (EnableLobbyChanges.Value)
            {
                On.RoR2.PreGameController.OnEnable += PreGameControllerOnOnEnable;
                On.RoR2.PreGameController.OnDisable += PreGameControllerOnOnDisable;
            }

            if (EnableGameOverChanges.Value)
            {
                On.RoR2.GameOverController.OnEnable += GameOverControllerOnOnEnable;
                On.RoR2.GameOverController.OnDisable += GameOverControllerOnOnDisable;
            }

            if (EnableCreditsChanges.Value)
            {
                On.RoR2.CreditsController.OnEnable += CreditsControllerOnOnEnable;
                On.RoR2.CreditsController.OnDisable += CreditsControllerOnOnDisable;
            }

            Log.LogDebug(nameof(Awake) + " done");
        }

        private void PreGameControllerOnOnEnable(PreGameController.orig_OnEnable orig, RoR2.PreGameController self)
        {
            orig(self);

            if (!UnityEngine.Application.isBatchMode)
            {
                On.RoR2.VoteController.ReceiveUserVote += LobbyVoteControllerOnReceiveUserVote;
            }
            else
            {
                Log.LogInfo("Running on dedicated server, " + nameof(LobbyVotesRequireHostReady) + " and " +
                            nameof(LobbyStartOnHostReady) + " disabled");
            }

            PreGameVoteController = self.GetComponent<RoR2.VoteController>();
            if (UnityEngine.Networking.NetworkServer.active)
            {
                PreGameVoteController.timeoutDuration = LobbyCountdownTime.Value;
                if (LobbyStartRequiresAllReady.Value)
                {
                    PreGameVoteController.timerStartCondition =
                        RoR2.VoteController.TimerStartCondition.WhileAllVotesReceived;
                }
            }

            Log.LogDebug(nameof(PreGameControllerOnOnEnable) + " done");
        }

        private void PreGameControllerOnOnDisable(PreGameController.orig_OnDisable orig, RoR2.PreGameController self)
        {
            if (!UnityEngine.Application.isBatchMode)
            {
                On.RoR2.VoteController.ReceiveUserVote -= LobbyVoteControllerOnReceiveUserVote;
            }

            PreGameVoteController = null;

            orig(self);

            Log.LogDebug(nameof(PreGameControllerOnOnDisable) + " done");
        }

        private void GameOverControllerOnOnEnable(GameOverController.orig_OnEnable orig, RoR2.GameOverController self)
        {
            orig(self);

            GameOverVoteController = self.GetComponent<RoR2.VoteController>();
            if (UnityEngine.Networking.NetworkServer.active)
            {
                GameOverVoteController.timeoutDuration = GameOverCountdownTime.Value;
                if (GameOverRequiresAllReady.Value)
                {
                    GameOverVoteController.timerStartCondition =
                        RoR2.VoteController.TimerStartCondition.WhileAllVotesReceived;
                }
            }

            Log.LogDebug(nameof(GameOverControllerOnOnEnable) + " done");
        }

        private void GameOverControllerOnOnDisable(GameOverController.orig_OnDisable orig, RoR2.GameOverController self)
        {
            GameOverVoteController = null;

            orig(self);

            Log.LogDebug(nameof(GameOverControllerOnOnDisable) + " done");
        }

        private void CreditsControllerOnOnEnable(CreditsController.orig_OnEnable orig, RoR2.CreditsController self)
        {
            orig(self);

            CreditsVoteController = self.GetComponent<RoR2.VoteController>();
            if (UnityEngine.Networking.NetworkServer.active)
            {
                CreditsVoteController.timeoutDuration = CreditsCountdownTime.Value;
                if (CreditsCountdownOnAnyReady.Value)
                {
                    CreditsVoteController.timerStartCondition =
                        RoR2.VoteController.TimerStartCondition.WhileAnyVoteReceived;
                }
            }

            Log.LogDebug(nameof(CreditsControllerOnOnEnable) + " done");
        }

        private void CreditsControllerOnOnDisable(CreditsController.orig_OnDisable orig, RoR2.CreditsController self)
        {
            CreditsVoteController = null;

            orig(self);

            Log.LogDebug(nameof(CreditsControllerOnOnDisable) + " done");
        }

        private void LobbyVoteControllerOnReceiveUserVote(VoteController.orig_ReceiveUserVote orig,
            RoR2.VoteController self, NetworkUser networkUser, int voteChoiceIndex)
        {
            Log.LogDebug(nameof(LobbyVoteControllerOnReceiveUserVote) +
                         $" called by: {networkUser.userName}({networkUser.id.value}), Vote Choice Index: {voteChoiceIndex}, Host: {networkUser.isLocalPlayer}");

            if (!PreGameVoteController)
            {
                Log.LogWarning(nameof(PreGameVoteController) + " null, calling " + nameof(orig));
                orig(self, networkUser, voteChoiceIndex);
                return;
            }

            if (!UnityEngine.Networking.NetworkServer.active)
            {
                Log.LogDebug("Not running on server, " + nameof(LobbyVoteControllerOnReceiveUserVote) +
                             " cancelled, calling " + nameof(orig));
                orig(self, networkUser, voteChoiceIndex);
                return;
            }

            if (!PreGameVoteController.Equals(self))
            {
                Log.LogDebug(nameof(PreGameVoteController) + " doesn't equal " + nameof(self) + ", calling " +
                             nameof(orig));
                orig(self, networkUser, voteChoiceIndex);
                return;
            }

            if (voteChoiceIndex != 0)
            {
                Log.LogDebug(nameof(voteChoiceIndex) + " doesn't equal 0, calling " + nameof(orig));
                orig(self, networkUser, voteChoiceIndex);
                return;
            }

            if (networkUser.isLocalPlayer)
            {
                if (LobbyStartOnHostReady.Value)
                {
                    self.StopTimer();
                    self.votes.Clear();

                    IEnumerable<NetworkUser> source = NetworkUser.readOnlyInstancesList;
                    if (self.onlyAllowParticipatingPlayers)
                    {
                        source = from v in source where v.isParticipating select v;
                    }

                    foreach (GameObject networkUserObject in from v in source select v.gameObject)
                    {
                        self.votes.Add(new UserVote { networkUserObject = networkUserObject, voteChoiceIndex = 0 });
                    }

                    Log.LogDebug("Setting everybody ready, num votes: " + self.votes.Count);
                    return;
                }

                if (LobbyVotesRequireHostReady.Value)
                {
                    Log.LogDebug("Accepting vote, " + nameof(networkUser) + " is server.");
                    orig(self, networkUser, voteChoiceIndex);
                    return;
                }

                orig(self, networkUser, voteChoiceIndex);
            }
            else
            {
                if (LobbyVotesRequireHostReady.Value)
                {
                    foreach (RoR2.UserVote v in self.votes)
                    {
                        if (NetworkUser.readOnlyInstancesList.Count > 0 &&
                            v.networkUserObject.Equals(NetworkUser.readOnlyInstancesList[0].gameObject) &&
                            v.voteChoiceIndex == 0)
                        {
                            orig(self, networkUser, voteChoiceIndex);
                        }
                    }
                }
                else
                {
                    orig(self, networkUser, voteChoiceIndex);
                }
            }
        }
    }
}