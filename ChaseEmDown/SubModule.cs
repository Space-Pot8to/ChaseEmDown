using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using MCM.Abstractions.Settings.Base.PerSave;
using MCM.Abstractions.FluentBuilder;
using MCM.Abstractions.FluentBuilder.Models;
using MCM.Abstractions.Settings.Base.Global;
using MCM.Abstractions.Ref;
using TaleWorlds.Library;

namespace ChaseEmDown
{
    public class SubModule : MBSubModuleBase
    {
        private FluentGlobalSettings _globalSettings;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();

        }

        private Action OnButtonClicked;
        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            ISettingsBuilder builder = BaseSettingsBuilder
                .Create("ChaseEmDown", "Chase Em Down Configuration")
                .SetFormat("xml")
                .SetFolderName("ChaseEmDown")
                .SetSubFolder("ChaseEmDown");
            OnButtonClicked = delegate ()
            {
                if (Campaign.Current != null)
                {
                    InformationManager.DisplayMessage(new InformationMessage("Click", new Color(134, 114, 250)));
                }
            };
            builder.CreateGroup("ChaseEmDown Settings",
                delegate (ISettingsPropertyGroupBuilder groupBuilder)
                {
                    Func<Action> func = () => OnButtonClicked;
                    Action<Action> action = delegate(Action x) { OnButtonClicked(); };

                    IRef ref1 = new ProxyRef<Action>(func, action);

                    groupBuilder.AddButton(
                        "reset_advanced_parties",
                        "Reset Advanced Parties",
                        ref1, "Test Button",
                        delegate (ISettingsPropertyButtonBuilder buttonBuilder)
                        {
                            buttonBuilder.SetHintText("Resets the AI of ordered parties. Must be in a campaign.");
                        });
                }
            );
            _globalSettings = builder.BuildAsGlobal();
            _globalSettings.Register();
        }

        protected override void InitializeGameStarter(Game game, IGameStarter gameStarterObject)
        {
            CampaignGameStarter starter = gameStarterObject as CampaignGameStarter;
            if (starter != null)
            {
                starter.AddBehavior(new SendAdvancedPartyCampaignBehavior());
            }
        }
    }

    public class SendAdvancedPartyCampaignBehavior : CampaignBehaviorBase
    {
        private SendAdvancedPartyDialog _dialogs;
        // form is AdvancedParty : TargetParty
        private Dictionary<MobileParty, AdvancedPartyTargeter> _advancedPartiesTargets;
        public Dictionary<MobileParty, AdvancedPartyTargeter> AdvancedPartiesTargets
        {
            get { return this._advancedPartiesTargets; }
        }

        public static SendAdvancedPartyCampaignBehavior Instance { get; private set; }
        public SendAdvancedPartyCampaignBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnSessionLaunched));
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnNewGameCreated));
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, new Action<MapEvent, PartyBase, PartyBase>(this.OnMapEventStarted));
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, new Action<MapEvent>(this.OnMapEventEnded));
            CampaignEvents.OnPartyJoinedArmyEvent.AddNonSerializedListener(this, new Action<MobileParty>(this.OnPartyJoinArmyEvent));
            CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, new Action<Army, Army.ArmyDispersionReason, bool>(this.OnArmyDispersed));
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, new Action<MobileParty>(this.HourlyPartyTick));
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData<Dictionary<MobileParty, AdvancedPartyTargeter>>("_advancedPartiesTargets", ref _advancedPartiesTargets);
        }

        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            _advancedPartiesTargets = new Dictionary<MobileParty, AdvancedPartyTargeter>();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _dialogs = new SendAdvancedPartyDialog(starter);
            _dialogs.Initialize();
            if (_advancedPartiesTargets == null)
                _advancedPartiesTargets = new Dictionary<MobileParty, AdvancedPartyTargeter>();
        }

        private void OnMapEventStarted(MapEvent mapEvent, PartyBase partyBase1, PartyBase partyBase2)
        {

        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            List<MobileParty> parties = mapEvent.InvolvedParties
                                        .Where(x => x.IsMobile
                                               && _advancedPartiesTargets.ContainsKey(x.MobileParty))
                                        .Select(x => x.MobileParty).ToList();
            if (MobileParty.MainParty.Army != null 
                && MobileParty.MainParty.Army.LeaderParty == MobileParty.MainParty) 
            {
                // after an advanced party has finished its battle with the target party,
                // its 
                foreach (MobileParty party in parties)
                {
                    party.Ai.EnableAi();
                    party.Army = MobileParty.MainParty.Army;
                }
            }
            else
            {
                foreach (MobileParty party in parties)
                {
                    party.Ai.EnableAi();
                }
            }
        }

        private void OnPartyJoinArmyEvent(MobileParty mobileParty)
        {
            // when an advanced party rejoins the army, their ai is reset and they are no longer tracked
            if (_advancedPartiesTargets.ContainsKey(mobileParty))
            {
                mobileParty.Ai.EnableAi();
                _advancedPartiesTargets.Remove(mobileParty);
            }
        }

        private void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
        {
            // when the player's army is dispersed, any advanced parties have their ai reset
            if (isPlayersArmy && _advancedPartiesTargets.Count > 0)
            {
                foreach(KeyValuePair<MobileParty, AdvancedPartyTargeter> keyValuePair in _advancedPartiesTargets)
                {
                    keyValuePair.Key.Ai.EnableAi();
                }
                _advancedPartiesTargets.Clear();
            }
        }

        private void HourlyPartyTick(MobileParty mobileParty)
        {
            AdvancedPartyTargeter targetAndOrder;
            bool tracked = _advancedPartiesTargets.TryGetValue(mobileParty, out targetAndOrder);
            if (tracked)
            {
                // if the advanced party goes out of sight, they return to the army
                if (targetAndOrder.Order == AdvancedPartyOrder.ChaseWhileVisible
                   && !mobileParty.IsVisible)
                {
                    mobileParty.Ai.EnableAi();
                    mobileParty.Army = MobileParty.MainParty.Army;
                }
            }
        }

        private class SendAdvancedPartyDialog
        {
            private CampaignGameStarter _starter;
            private List<MobileParty> _nearbyParties;
            private List<ConversationSentence> _availableTargets;
            private MobileParty? _targetParty;
            private AdvancedPartyOrder _order;

            public SendAdvancedPartyDialog(CampaignGameStarter starter)
            {
                _starter = starter;
                _nearbyParties = new List<MobileParty>();
                _availableTargets = new List<ConversationSentence>();
            }

            private bool TargetPartyCondition()
            {
                return true;
            }

            private void TargetPartyConsequence()
            {
                int currentSentenceNo = (int)typeof(ConversationManager).GetField("_currentSentence", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Campaign.Current.ConversationManager);
                List<ConversationSentence> sentences = (List<ConversationSentence>)typeof(ConversationManager).GetField("_sentences", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Campaign.Current.ConversationManager);
                ConversationSentence sentence = sentences[currentSentenceNo];
                _targetParty = (MobileParty)sentence.RelatedObject;
            }

            public void Initialize()
            {
                _starter.AddPlayerLine(
                "hero_send_scout_party",
                "hero_main_options",
                "lord_advanced_party_response",
                "I want you to attack and hold an enemy party until reinforcements can arrive.",
                delegate ()
                {
                    MobileParty mainParty = MobileParty.MainParty;
                    Hero talkTo = Hero.OneToOneConversationHero;
                    Hero mainHero = Hero.MainHero;

                    _nearbyParties = MobileParty.All.Where(
                        x => x.IsVisible
                          && x.MapFaction.IsAtWarWith(Hero.MainHero.MapFaction)
                          && x.IsLordParty).ToList();

                    return mainParty.Army != null // player is in army
                        && talkTo.PartyBelongedTo != null // talkTo has a party
                        && talkTo.PartyBelongedTo.LeaderHero == talkTo // talkTo is the leader of that party
                        && mainParty.Army.LeaderPartyAndAttachedParties.Contains(talkTo.PartyBelongedTo) // talkTo's party is in player's army
                        && _nearbyParties.Count > 0; // there are nearby enemy lord parties
                },
                delegate ()
                {
                    for (int i = 0; i < _availableTargets.Count; i++)
                    {
                        Campaign.Current.ConversationManager.RemoveRelatedLines(_availableTargets[i].RelatedObject);
                    }
                    _availableTargets.Clear();

                    for (int i = 0; i < _nearbyParties.Count; i++)
                    {
                        MobileParty party = _nearbyParties[i];
                        string optionId = "player_pick_army_" + i;
                        ConversationSentence sentence = _starter.AddPlayerLine(
                            optionId, 
                            "player_advanced_party_select_target",
                            "lord_chase_down_how_far",
                            "I want you to chase down {TARGET_ARMY_" + i + "}", 
                            new ConversationSentence.OnConditionDelegate(TargetPartyCondition), 
                            new ConversationSentence.OnConsequenceDelegate(TargetPartyConsequence), 200);
                        typeof(ConversationSentence).GetProperty("RelatedObject").SetValue(sentence, party, null);
                        _availableTargets.Add(sentence);
                        MBTextManager.SetTextVariable("TARGET_ARMY_" + i, _nearbyParties[i].Name);
                    }
                });

                _starter.AddDialogLine(
                    "lord_advanced_party_response_which_one", 
                    "lord_advanced_party_response", 
                    "player_advanced_party_select_target",
                    "Very well. Which party would you like me to attack?",
                    delegate ()
                    {
                        return _nearbyParties.Count > 0;
                    },
                    delegate ()
                    {
                        
                    });

                _starter.AddPlayerLine(
                        "player_pick_target_quit",
                        "player_advanced_party_select_target",
                        "lord_pretalk",
                        "{=D33fIGQe}Never mind.", null, null);

                _starter.AddDialogLine(
                    "lord_chase_down_how_far",
                    "lord_chase_down_how_far",
                    "player_set_lord_chase_option",
                    "For how long should I chase the target?",
                    null,
                    delegate ()
                    {

                    });

                _starter.AddPlayerLine(
                    "player_lord_chase_while_visible",
                    "player_set_lord_chase_option",
                    "lord_accept_chase_down",
                    "Chase them until you are out of sight.",
                    null,
                    delegate ()
                    {
                        _order = AdvancedPartyOrder.ChaseWhileVisible;
                    });

                _starter.AddPlayerLine(
                    "player_lord_chase_always",
                    "player_set_lord_chase_option",
                    "lord_accept_chase_down",
                    "Chase them to the ends of the earth.",
                    null,
                    delegate ()
                    {
                        _order = AdvancedPartyOrder.ChaseAlways;
                    });

                _starter.AddPlayerLine(
                        "player_lord_chase_quit",
                        "player_set_lord_chase_option",
                        "lord_pretalk",
                        "{=D33fIGQe}Never mind.", null, null);

                _starter.AddDialogLine(
                    "lord_accept_chase_down", 
                    "lord_accept_chase_down", 
                    "hero_main_options", 
                    "{=dzXaXKaC}Very well.",
                    delegate ()
                    {
                        return true;
                    },
                    delegate ()
                    {
                        MobileParty advancedParty = Hero.OneToOneConversationHero.PartyBelongedTo;
                        advancedParty.Army = null;
                        advancedParty.Ai.DisableAi();
                        advancedParty.SetMoveEngageParty(_targetParty);
                        SendAdvancedPartyCampaignBehavior.Instance.AdvancedPartiesTargets
                        .Add(advancedParty, new AdvancedPartyTargeter() { Order = _order, TargetParty = _targetParty });
                        _targetParty = null;
                        _order = AdvancedPartyOrder.Invalid;
                    });
            }
        }

        public enum AdvancedPartyOrder
        {
            Invalid,
            ChaseAlways,
            ChaseWhileVisible
        }

        public struct AdvancedPartyTargeter
        {
            public MobileParty TargetParty;
            public AdvancedPartyOrder Order;
        }

        public class SendAdvancedPartyCampaignBehaviorTypeDefiner : CampaignBehaviorBase.SaveableCampaignBehaviorTypeDefiner
        {
            public SendAdvancedPartyCampaignBehaviorTypeDefiner() : base(57501812)
            {
                
            }
            protected override void DefineContainerDefinitions()
            {
                base.ConstructContainerDefinition(typeof(AdvancedPartyOrder));
                base.ConstructContainerDefinition(typeof(AdvancedPartyTargeter));
                base.ConstructContainerDefinition(typeof(Dictionary<MobileParty, AdvancedPartyTargeter>));
            }

            protected override void DefineEnumTypes()
            {
                base.AddEnumDefinition(typeof(AdvancedPartyOrder), 1, null);
            }

            protected override void DefineStructTypes()
            {
                base.AddStructDefinition(typeof(AdvancedPartyTargeter), 2, null);
            }
        }
    }

    public class ChaseEmDownSettings : AttributePerSaveSettings<ChaseEmDownSettings>
    {
        public override string Id => throw new NotImplementedException();

        public override string DisplayName => throw new NotImplementedException();
    }
}