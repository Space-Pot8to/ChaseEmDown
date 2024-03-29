﻿using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem.Actions;

using MCM.Abstractions.FluentBuilder;
using MCM.Abstractions.FluentBuilder.Models;
using MCM.Abstractions.Base.Global;
using MCM.Common;
#if E181_OR_LOWER
using MCM.Abstractions.Settings.Base.Global;
using MCM.Abstractions.Ref;
#endif

using HarmonyLib;

namespace ChaseEmDown
{   
    /// <summary>
    /// Simple idea. It's very annoying that when you are in an army of 1000, but chasing an army 
    /// of 500, you can't send out a party to pin down the enemy until you arrive. The smaller 
    /// army will usually be faster than you, so there's no way to bring them to battle.
    /// </summary>
    public class SubModule : MBSubModuleBase
    {
        private Harmony HarmonyInstance;
        private FluentGlobalSettings? _globalSettings;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            HarmonyInstance = new Harmony("ChaseEmDown.Harmony");
            HarmonyInstance.PatchAll();
        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();

        }
        private Action ResetPartiesButton { get; set; } = delegate () { };
        private bool AvoidCrashDoesNothing { get; set; } = true;
        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            #region MCM Settings
            ISettingsBuilder settingsBuilder = BaseSettingsBuilder
                .Create("ChaseEmDown", new TextObject("{=GVppfCoKHi}Chase Em Down Configuration").ToString())
                .SetFormat("xml")
                .SetFolderName("ChaseEmDown")
                .SetSubFolder("ChaseEmDown");
            ResetPartiesButton = delegate ()
            {
                if (Campaign.Current != null)
                {
                    SendAdvancedPartyCampaignBehavior.Instance.EnableAllAIsAndReturnToArmy();
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=U3VavjmS86}No, really, you need to be in campaign for this to do anyting.").ToString(), new Color(134, 114, 250)));
                }
            };
            settingsBuilder.CreateGroup(new TextObject("{=23Bylwmnob}ChaseEmDown Settings").ToString(),
                delegate (ISettingsPropertyGroupBuilder groupBuilder)
                {
                    Func<Action> func = () => ResetPartiesButton;
                    Action<Action> action = delegate(Action x) { ResetPartiesButton(); };
                    IRef ref1 = new ProxyRef<Action>(func, action);

                    groupBuilder.AddButton(
                        "ResetPartiesButton",
                        new TextObject("{=ApJERVtREW}Reset Advanced Parties").ToString(),
                        ref1, new TextObject("{=ApJERVtREW}Reset Advanced Parties").ToString(),
                        delegate (ISettingsPropertyButtonBuilder buttonBuilder)
                        {
                            buttonBuilder
                            .SetHintText(new TextObject("{=v2BGSMibjm}Reset the AI of ordered parties. Must be in a campaign.").ToString())
                            .SetRequireRestart(true);
                        });

                    Func<bool> func2 = () => AvoidCrashDoesNothing;
                    Action<bool> action2 = delegate (bool x) { AvoidCrashDoesNothing = x; };
                    IRef ref2 = new ProxyRef<bool>(func2, action2);

                    groupBuilder.AddBool(
                        "AvoidCrashDoesNothing",
                        new TextObject("{=aP5YVOl7XV}Avoids Crash Does Nothing").ToString(),
                        ref2,
                        delegate (ISettingsPropertyBoolBuilder boolBuilder)
                        {
                            boolBuilder
                            .SetHintText(new TextObject("{=uTlQv041DB}This is just here to make sure MCM doesn't crash, it does nothing.").ToString())
                            .SetRequireRestart(true);
                        });
                }
            );
            _globalSettings = settingsBuilder.BuildAsGlobal();
            _globalSettings.Register();
            #endregion
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

        /// <summary>
        /// Re-enables the ai of advanced parties and returns them to the army. Should only be 
        /// used from the mod options menu.
        /// </summary>
        public void EnableAllAIsAndReturnToArmy()
        {
            for (int i = 0; i < _advancedPartiesTargets.Count; i++)
            {
                MobileParty party = _advancedPartiesTargets.ElementAt(i).Key;
                party.Ai.EnableAi();
                if (MobileParty.MainParty != null && MobileParty.MainParty.Army != null)
                {
                    party.SetMoveEscortParty(MobileParty.MainParty);
                    party.Army = MobileParty.MainParty.Army;
                }
            }
            _advancedPartiesTargets.Clear();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnSessionLaunched));
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnNewGameCreated));
            CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, new Action<Army, Army.ArmyDispersionReason, bool>(this.OnArmyDispersed));
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, new Action<MobileParty>(this.HourlyPartyTick));
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, new Action<MobileParty, PartyBase>(this.OnMobilePartyDestroyed));
            CampaignEvents.PartyAttachedAnotherParty.AddNonSerializedListener(this, new Action<MobileParty>(this.OnPartyAttachedAnotherParty));
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

        private void OnMobilePartyDestroyed(MobileParty mobileParty, PartyBase partyBase)
        {
            // if mobileParty is an advanced party, then remove it from the dictionary
            if (_advancedPartiesTargets.ContainsKey(mobileParty))
            {
                _advancedPartiesTargets.Remove(mobileParty);
                return;
            }

            // if the player's party is destroyed, then reset tracked ai parties and
            // clear _advancedPartiesTargets
            if (mobileParty.IsMainParty)
            {
                List<MobileParty> tracked2 = _advancedPartiesTargets
                                        .Select(x => x.Key).ToList();
                foreach (MobileParty party in tracked2)
                    party.Ai.EnableAi();

                _advancedPartiesTargets.Clear();
                return;
            }

            // when the target party is destroyed for whatever reason, return the chasing
            // party to the player's army
            List<MobileParty> tracked = _advancedPartiesTargets
                                        .Where(x => x.Value.TargetParty == mobileParty)
                                        .Select(x => x.Key).ToList();

            foreach (MobileParty party in tracked)
            {
                SetNewOrder(party, new AdvancedPartyTargeter()
                {
                    Order = AdvancedPartyOrder.ReturnToArmy,
                    TargetParty = MobileParty.MainParty
                });
            }
        }

        private void OnPartyAttachedAnotherParty(MobileParty mobileParty)
        {
            // when an advanced party re-attaches to the army (no re-joins), their ai is reset and
            // they are removed from the dictionary
            if (_advancedPartiesTargets.ContainsKey(mobileParty))
            {
                mobileParty.Ai.EnableAi();
                _advancedPartiesTargets.Remove(mobileParty);
            }
        }

        private void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
        {
            // when the player's army is dispersed, any advanced parties have their ai reset
            // and they are removed from the dictionary
            if (army.LeaderParty == MobileParty.MainParty
                && _advancedPartiesTargets.Count > 0)
            {
                foreach (KeyValuePair<MobileParty, AdvancedPartyTargeter> kv in _advancedPartiesTargets)
                {
                    kv.Key.Ai.EnableAi();
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
                // if the advanced party goes out of sight, and they have been ordered to stay in
                // sight they start to return to the army so remove them from the tracked parties
                // as well
                bool partyGetsOutOfRange = targetAndOrder.Order == AdvancedPartyOrder.ChaseWhileVisible
                   && !mobileParty.IsVisible;

                // if for some reason the target is destroyed before we catch it, send the
                // advanced party back to the army
                bool targetPartyGetsSwallowed = (targetAndOrder.Order == AdvancedPartyOrder.ChaseWhileVisible
                        || targetAndOrder.Order == AdvancedPartyOrder.ChaseAlways)
                        && targetAndOrder.TargetParty != null
                        && !MobileParty.All.Contains(targetAndOrder.TargetParty);

                // if the target party has entered a walled settlement, tell the pursuer to return
                bool targetPartyEntersFort = (targetAndOrder.Order == AdvancedPartyOrder.ChaseWhileVisible
                        || targetAndOrder.Order == AdvancedPartyOrder.ChaseAlways)
                        && targetAndOrder.TargetParty.CurrentSettlement != null
                        && targetAndOrder.TargetParty.CurrentSettlement.IsFortification;

                if (partyGetsOutOfRange 
                    || targetPartyGetsSwallowed
                    || targetPartyEntersFort)
                {
                    SetNewOrder(mobileParty, new AdvancedPartyTargeter()
                    {
                        Order = AdvancedPartyOrder.ReturnToArmy,
                        TargetParty = MobileParty.MainParty
                    });
                }
                // if the party has been ordered to give chase, have them leave the settlement
                else if ((targetAndOrder.Order == AdvancedPartyOrder.ChaseWhileVisible
                        || targetAndOrder.Order == AdvancedPartyOrder.ChaseAlways)
                        && mobileParty.CurrentSettlement != null)
                {
                    LeaveSettlementAction.ApplyForParty(mobileParty);
                }
            }
        }

        /// <summary>
        /// Sets a new order for an advanced party.
        /// </summary>
        /// <param name="party"></param>
        /// <param name="targeter"></param>
        public void SetNewOrder(MobileParty party, AdvancedPartyTargeter targeter)
        {
            if (targeter.Order == AdvancedPartyOrder.ChaseWhileVisible
                || targeter.Order == AdvancedPartyOrder.ChaseAlways)
            {
                party.Army = null;
                party.Ai.DisableAi();
                party.SetMoveEngageParty(targeter.TargetParty);
                _advancedPartiesTargets[party] = targeter;
            }
            else if (targeter.Order == AdvancedPartyOrder.ReturnToArmy)
            {
                party.Army = targeter.TargetParty.Army;
                party.Ai.EnableAi();
                party.SetMoveEscortParty(targeter.TargetParty);
                party.Ai.RethinkAtNextHourlyTick = true;
                _advancedPartiesTargets[party] = targeter;
            }
        }

        private class SendAdvancedPartyDialog
        {
            private CampaignGameStarter _starter;
            private List<MobileParty> _nearbyParties;
            private MobileParty? _targetParty;
            private AdvancedPartyOrder _order;

            public SendAdvancedPartyDialog(CampaignGameStarter starter)
            {
                _starter = starter;
                _nearbyParties = new List<MobileParty>();
                _reorderParties = new List<MobileParty>();
            }

            public List<MobileParty> GetNearbyParties()
            {
                List<MobileParty> nearby = MobileParty.All
                    .Where(
                        x => x.IsVisible // player can see the party
                          && x.MapFaction.IsAtWarWith(Hero.MainHero.MapFaction) // party is an enemy
                          && x.IsLordParty // party is a lord party (so we don't have a list of 100 looter parties)
                          && (
                                // either party is the leader of his army 
                                // or party is not yet attached to his army
                                (x.Army != null && (x.Army.LeaderParty == x || !x.Army.LeaderPartyAndAttachedParties.Contains(x)))
                                || x.Army == null // or party is not in an army
                             )
                          && x.CurrentSettlement == null // party is not in settlement
                          )
                    .OrderBy(x => x.Position2D.Distance(MobileParty.MainParty.Position2D))
                    .ToList();

                return nearby;
            }

            /// <summary>
            /// This dialog is for sending a new army party member to chase down an enemy lord 
            /// party. The player can specify whether the party should chase the enemy forever, 
            /// or just until the party is out of sight.
            /// </summary>
#region Send Advanced Party Dialog
            public void InitializeSendAdvancedPartyDialog()
            {
                _starter.AddPlayerLine(
                    "player_army_orders",
                    "hero_main_options",
                    "lord_army_task_response",
                    "{=5n2zCokEK8}As general of this army I have a new task for you.",
                    new ConversationSentence.OnConditionDelegate(player_army_orders_condition),
                    null, 200,
                    delegate (out TextObject explanation)
                    {
                        Hero talkTo = Hero.OneToOneConversationHero;
                        // if player sends out the last party, the army will disband, so disallow this
                        bool lastPartyInArmy = talkTo.PartyBelongedTo.Army.LeaderPartyAndAttachedParties.Count() < 3;
                        if (lastPartyInArmy)
                        {
                            explanation = new TextObject("{=XBMCcppr1o}If this party is sent out the army will disband!");
                            return false;
                        }

                        explanation = TextObject.Empty;
                        return true;
                    });

                bool player_army_orders_condition()
                {
                    _targetParty = null;
                    _order = AdvancedPartyOrder.Invalid;

                    MobileParty mainParty = MobileParty.MainParty;
                    Hero talkTo = Hero.OneToOneConversationHero;
                    Hero mainHero = Hero.MainHero;

                    return mainParty.Army != null // player is in army
                        && mainParty.Army.LeaderParty == mainParty // player is leader of that army
                        && talkTo.PartyBelongedTo != null // talkTo has a party
                        && talkTo.PartyBelongedTo.LeaderHero == talkTo // talkTo is the leader of that party
                        && mainParty.Army.LeaderPartyAndAttachedParties.Contains(talkTo.PartyBelongedTo); // talkTo's party is in player's army
                }

                _starter.AddDialogLine(
                    "lord_army_task_response",
                    "lord_army_task_response",
                    "player_give_army_task_options",
                    "{=k87hqeEpVy}Yes general. What would you have me do?",
                    delegate () { return true; },
                    null
                    );

                _starter.AddPlayerLine(
                    "hero_send_advanced_party",
                    "player_give_army_task_options",
                    "lord_advanced_party_response",
                    "{=QFUphXTfPi}I want you to attack and hold down an enemy party until reinforcements can arrive.",
                new ConversationSentence.OnConditionDelegate(hero_send_advanced_party_condition),
                new ConversationSentence.OnConsequenceDelegate(hero_send_advanced_party_consequence),
                100,
                new ConversationSentence.OnClickableConditionDelegate(hero_send_advanced_party_can_click));

                bool hero_send_advanced_party_condition()
                {
                    _nearbyParties = GetNearbyParties();
                    return true;
                }
                void hero_send_advanced_party_consequence()
                {
                    ConversationSentence.SetObjectsToRepeatOver(_nearbyParties, 5);
                }
                bool hero_send_advanced_party_can_click(out TextObject explanation)
                {
                    Hero talkTo = Hero.OneToOneConversationHero;

                    // party is not already assigned to chase down another party
                    bool alreadyAssigned = SendAdvancedPartyCampaignBehavior
                                            .Instance
                                            .AdvancedPartiesTargets
                                            .ContainsKey(talkTo.PartyBelongedTo);

                    // there are nearby enemy lord parties
                    bool noTargets = _nearbyParties.Count == 0;

                    explanation = TextObject.Empty;
                    if (alreadyAssigned)
                    {
                        explanation = new TextObject("{=TXxrkZkg4Z}This party already has an assignment.");
                        return false;
                    }
                    else if (noTargets)
                    {
                        explanation = new TextObject("{=hzjqdBmApt}There are no enemy lord parties in the area.");
                        return false;
                    }
                    return true;
                }

                _starter.AddPlayerLine(
                    "hero_send_advanced_party_cancel",
                    "player_give_army_task_options",
                    "lord_pretalk",
                    "{=D33fIGQe}Never mind.",
                    null,
                    null);

                _starter.AddDialogLine(
                    "lord_advanced_party_response_which_one",
                    "lord_advanced_party_response",
                    "player_advanced_party_select_target",
                    "{=RWVHqhyo69}Very well. Which party would you like me to attack?",
                    delegate ()
                    {
                        return _nearbyParties.Count > 0;
                    },
                    null);

                /** Don't think this will ever be hit
                _starter.AddDialogLine(
                    "lord_advanced_party_response_no_targets",
                    "lord_advanced_party_response",
                    "lord_pretalk",
                    "{=D33fIGQe}Never mind.",
                    delegate () { return _nearbyParties.Count == 0; },
                    null);
                */

                _starter.AddRepeatablePlayerLine(
                    "player_pick_target_party",
                    "player_advanced_party_select_target",
                    "lord_chase_down_how_far",
                    "{=WAiHYoYk79}I want you to attack {=!}{TARGET_PARTY}",
                    "{=FR1XE0ZqGZ}I am thinking of a different party.",
                    "lord_advanced_party_response",
                    delegate ()
                    {
                        MobileParty mobileParty = ConversationSentence.CurrentProcessedRepeatObject as MobileParty;
                        if (mobileParty.Army != null && mobileParty.Army.LeaderParty == mobileParty)
                            ConversationSentence.SelectedRepeatLine.SetTextVariable("TARGET_PARTY", mobileParty.Army.Name);
                        else
                            ConversationSentence.SelectedRepeatLine.SetTextVariable("TARGET_PARTY", mobileParty.Name);
                        return true;
                    },
                    delegate ()
                    {
                        _targetParty = ConversationSentence.SelectedRepeatObject as MobileParty;
                    }
                    );

                _starter.AddPlayerLine(
                    "player_pick_target_quit",
                    "player_advanced_party_select_target",
                    "lord_pretalk",
                    "{=D33fIGQe}Never mind.", null, null);

                _starter.AddDialogLine(
                    "lord_chase_down_how_far",
                    "lord_chase_down_how_far",
                    "player_set_lord_chase_option",
                    "{=FDvkdXfW4Q}For how long should I chase the {TARGET_PARTY}?",
                    delegate ()
                    {
                        MBTextManager.SetTextVariable("TARGET_PARTY", _targetParty.Name);
                        if (_targetParty.Army != null && _targetParty.Army.LeaderParty == _targetParty)
                            ConversationSentence.SelectedRepeatLine.SetTextVariable("TARGET_PARTY", _targetParty.Army.Name);
                        else
                            ConversationSentence.SelectedRepeatLine.SetTextVariable("TARGET_PARTY", _targetParty.Name);
                        return true;
                    },
                    null);

                _starter.AddPlayerLine(
                    "player_lord_chase_while_visible",
                    "player_set_lord_chase_option",
                    "lord_accept_chase_down",
                    "{=iDLpYYNuHz}Chase them until you are out of sight. If you haven't caught them by then, return.",
                    null,
                    delegate ()
                    {
                        _order = AdvancedPartyOrder.ChaseWhileVisible;
                    });

                _starter.AddPlayerLine(
                    "player_lord_chase_always",
                    "player_set_lord_chase_option",
                    "lord_accept_chase_down",
                    "{=AW3imsxKDO}Chase them to the ends of the earth.",
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
                    null,
                    delegate ()
                    {
                        MobileParty advancedParty = Hero.OneToOneConversationHero.PartyBelongedTo;
                        AdvancedPartyTargeter targeter = new AdvancedPartyTargeter()
                        {
                            Order = _order,
                            TargetParty = _targetParty
                        };

                        SendAdvancedPartyCampaignBehavior.Instance.SetNewOrder(advancedParty, targeter);
                    });
            }
#endregion

            /// <summary>
            /// This dialog is for giver a new order to a detached party. 
            /// </summary>
#region New Orders Dialog
            private List<MobileParty> _reorderParties;
            public void InitializeReorderAdvancedPartiesDialog()
            {   
                _starter.AddPlayerLine(
                    "player_advanced_parties_army_member_start",
                    "hero_main_options",
                    "army_member_reorder_parties_response",
                    "{=BU5St3BCBD}I have new orders for the parties I have dispatched.",
                    new ConversationSentence.OnConditionDelegate(player_advanced_parties_army_member_start_condition),
                    null, 200,
                    delegate (out TextObject explanation)
                    {
                        // disable click if the 
                        if(SendAdvancedPartyCampaignBehavior.Instance.AdvancedPartiesTargets.Count() == 0)
                        {
                            explanation = new TextObject("{=r5AH0Bpzed}There are no parties out right now.");
                            return false;
                        }
                        explanation = TextObject.Empty;
                        return true;
                    });

                bool player_advanced_parties_army_member_start_condition()
                {
                    _targetParty = null;
                    _order = AdvancedPartyOrder.Invalid;
                    Hero talkTo = Hero.OneToOneConversationHero;
                    return (talkTo.IsLord || talkTo.IsPlayerCompanion) // talkTo is either a lord or player companion
                        && MobileParty.MainParty != null // player party exists
                        && MobileParty.MainParty.Army != null // player party army exists
                        && MobileParty.MainParty.Army.LeaderParty == MobileParty.MainParty // player is leader of the army
                        && talkTo.PartyBelongedTo != null // talkTo is in a party
                        && MobileParty.MainParty.Army.LeaderPartyAndAttachedParties.Contains(talkTo.PartyBelongedTo) // talkTo's party is in the army
                        && !talkTo.IsPrisoner; // talkTo is not a prisoner
                }

#region Select New Order Party or Parties
                _starter.AddDialogLine(
                    "army_member_reorder_parties_response_multiple_out",
                    "army_member_reorder_parties_response",
                    "player_reorder_one_or_all_parties",
                    "{=0ayP9BwAYA}An order to all dispatched parties or just one?",
                    delegate ()
                    {
                        return SendAdvancedPartyCampaignBehavior.Instance.AdvancedPartiesTargets.Count() > 1;
                    },
                    null);

                _starter.AddDialogLine(
                    "army_member_reorder_parties_response_one_out",
                    "army_member_reorder_parties_response",
                    "player_select_new_order",
                    "{=iWNQ05qzKy}{ADVANCED_PARTY} is the only party we have out. What is the new order?",
                    delegate ()
                    {
                        if (SendAdvancedPartyCampaignBehavior.Instance.AdvancedPartiesTargets.Count() == 1)
                        {
                            _reorderParties = SendAdvancedPartyCampaignBehavior.Instance.AdvancedPartiesTargets.Keys.Take(1).ToList();
                            MBTextManager.SetTextVariable("ADVANCED_PARTY", _reorderParties[0].Name);
                            return true;
                        }
                        return false;
                    },
                    null);

                _starter.AddPlayerLine(
                    "player_reorder_all_parties",
                    "player_reorder_one_or_all_parties",
                    "army_member_ask_which_order",
                    "{=Wg5hQsdFD7}I want to send a new order to all the dispatched parties.",
                    null,
                    delegate ()
                    {
                        _reorderParties = SendAdvancedPartyCampaignBehavior.Instance.AdvancedPartiesTargets.Keys.ToList();
                    });

                _starter.AddPlayerLine(
                    "player_reorder_one_party",
                    "player_reorder_one_or_all_parties",
                    "army_member_ask_which_single_party",
                    "{=vx6bgALWUe}I want to send a new order to a single party.",
                    null,
                    delegate ()
                    {
                        ConversationSentence
                        .SetObjectsToRepeatOver
                        (SendAdvancedPartyCampaignBehavior.Instance.AdvancedPartiesTargets.Keys.ToList(), 5);
                    });

                _starter.AddPlayerLine(
                    "player_reorder_parties_cancel",
                    "player_reorder_one_or_all_parties",
                    "lord_pretalk",
                    "{=D33fIGQe}Never mind.",
                    null, null);

                _starter.AddDialogLine(
                    "army_member_ask_which_single_party",
                    "army_member_ask_which_single_party",
                    "player_select_party_to_reorder",
                    "{=m86uT9Vsd2}Which party would you like to give new orders?",
                    null, null);

                _starter.AddRepeatablePlayerLine(
                    "player_select_party_to_reorder",
                    "player_select_party_to_reorder",
                    "army_member_ask_which_order",
                    "{=egHCPrI9fP}I want to give a new order to {=!}{ADVANCED_PARTY}.",
                    "{=FR1XE0ZqGZ}I am thinking of a different party.",
                    "army_member_ask_which_single_party",
                    delegate ()
                    {
                        MobileParty mobileParty = ConversationSentence.CurrentProcessedRepeatObject as MobileParty;
                        ConversationSentence.SelectedRepeatLine.SetTextVariable("ADVANCED_PARTY", mobileParty.Name);
                        return true;
                    },
                    delegate ()
                    {
                        _reorderParties = new List<MobileParty>() { ConversationSentence.SelectedRepeatObject as MobileParty };
                    }, 100,
                    delegate (out TextObject explanation)
                    {
                        MobileParty mobileParty = ConversationSentence.CurrentProcessedRepeatObject as MobileParty;
                        explanation = explanation = TextObject.Empty;
                        GetAdvancedPartyHoverText(mobileParty, out explanation);
                        return true;
                    });

                void GetAdvancedPartyHoverText(MobileParty mobileParty, out TextObject explanation)
                {
                    AdvancedPartyTargeter targeter = SendAdvancedPartyCampaignBehavior.Instance.AdvancedPartiesTargets[mobileParty];
                    switch (targeter.Order)
                    {
                        case AdvancedPartyOrder.ReturnToArmy:
                            explanation = new TextObject("{=3EMhi4DJHD}Currently returning to the army.");
                            break;
                        case AdvancedPartyOrder.ChaseAlways:
                            explanation = new TextObject("{=xhgdBh17XH}Currently chasing {TARGET_PARTY} to the ends of the earth.");
                            explanation.SetTextVariable("TARGET_PARTY", targeter.TargetParty.Name);
                            break;
                        case AdvancedPartyOrder.ChaseWhileVisible:
                            explanation = new TextObject("{=hY8fLCLY1E}Currently chasing {TARGET_PARTY} while in sight.");
                            explanation.SetTextVariable("TARGET_PARTY", targeter.TargetParty.Name);
                            break;
                        default:
                            explanation = new TextObject("***ERROR in GetAdvancedPartyHoverText***");
                            break;
                    }
                }

                _starter.AddPlayerLine(
                    "player_select_party_to_reorder_cancel",
                    "player_select_party_to_reorder",
                    "lord_pretalk",
                    "{=D33fIGQe}Never mind.",
                    null, null);
#endregion

#region Select New Order
                _starter.AddDialogLine(
                    "army_member_ask_which_order",
                    "army_member_ask_which_order",
                    "player_select_new_order",
                    "{=BcgseTiOd9}And what is the new order?",
                    null, null);

                
                _starter.AddPlayerLine(
                    "player_select_return_to_army",
                    "player_select_new_order",
                    "army_member_send_out_new_orders",
                    "{=Cy4CfytqnS}Tell {SELECTED_PARTIES_TEXT} to return to the army immediately.",
                    delegate ()
                    {
                        if(_reorderParties.Count == 1)
                            MBTextManager.SetTextVariable("SELECTED_PARTIES_TEXT", _reorderParties[0].Name);
                        else
                            MBTextManager.SetTextVariable("SELECTED_PARTIES_TEXT", "all advanced parties");
                        return true;
                    },
                    delegate ()
                    {
                        _order = AdvancedPartyOrder.ReturnToArmy;
                        _targetParty = MobileParty.MainParty;
                    }, 100,
                    delegate (out TextObject explanation)
                    {
                        explanation = TextObject.Empty;
                        if (_reorderParties.Count == 1)
                        {
                            MobileParty mobileParty = _reorderParties[0];
                            GetAdvancedPartyHoverText(mobileParty, out explanation);
                        }
                        return true;
                    });

                _starter.AddPlayerLine(
                    "player_select_attack_new_party",
                    "player_select_new_order",
                    "army_member_ask_for_new_target",
                    "{=kydSZGhLgt}Tell {SELECTED_PARTIES_TEXT} to attack a new party.",
                    delegate ()
                    {
                        _nearbyParties = GetNearbyParties();
                        if (_reorderParties.Count == 1)
                            MBTextManager.SetTextVariable("SELECTED_PARTIES_TEXT", _reorderParties[0].Name);
                        else
                            MBTextManager.SetTextVariable("SELECTED_PARTIES_TEXT", "{=yeHERxwgaE}all the advanced parties");
                        return true;
                    },
                    delegate ()
                    {
                        ConversationSentence.SetObjectsToRepeatOver(_nearbyParties, 5);
                    }, 100,
                    delegate (out TextObject explanation)
                    {
                        explanation = TextObject.Empty;
                        if (_nearbyParties.Count == 0)
                        {
                            MobileParty mobileParty = _reorderParties[0];
                            explanation = new TextObject("{=hzjqdBmApt}There are no enemy lord parties in the area.");
                            return false;
                        }
                        return true;
                    });

                _starter.AddPlayerLine(
                    "player_select_new_order_cancel",
                    "player_select_new_order",
                    "lord_pretalk",
                    "{=D33fIGQe}Never mind.",
                    null, null);
#endregion

#region Select New Target
                _starter.AddDialogLine(
                    "army_member_ask_for_new_target",
                    "army_member_ask_for_new_target",
                    "player_select_new_target",
                    "{=jsI9InJEuY}Which party should {SELECTED_PARTIES_TEXT} attack?",
                    delegate ()
                    {
                        if (_reorderParties.Count == 1)
                            MBTextManager.SetTextVariable("SELECTED_PARTIES_TEXT", _reorderParties[0].Name);
                        else
                            MBTextManager.SetTextVariable("SELECTED_PARTIES_TEXT", "they");
                        return true;
                    },
                    null
                    );

                _starter.AddRepeatablePlayerLine(
                    "player_select_new_target",
                    "player_select_new_target",
                    "army_member_ask_chase_down_how_far",
                    "{=las7dgZjpL}I want {SELECTED_PARTIES_TEXT} to attack {=!}{TARGET_PARTY}.",
                    "{=FR1XE0ZqGZ}I am thinking of a different party.",
                    "army_member_ask_for_new_target",
                    delegate ()
                    {
                        if (_reorderParties.Count == 1)
                            ConversationSentence.SelectedRepeatLine.SetTextVariable("SELECTED_PARTIES_TEXT", _reorderParties[0].Name);
                        else
                            ConversationSentence.SelectedRepeatLine.SetTextVariable("SELECTED_PARTIES_TEXT", "them");

                        MobileParty mobileParty = ConversationSentence.CurrentProcessedRepeatObject as MobileParty;
                        if (mobileParty.Army != null && mobileParty.Army.LeaderParty == mobileParty)
                            ConversationSentence.SelectedRepeatLine.SetTextVariable("TARGET_PARTY", mobileParty.Army.Name);
                        else
                            ConversationSentence.SelectedRepeatLine.SetTextVariable("TARGET_PARTY", mobileParty.Name);
                        return true;
                    },
                    delegate ()
                    {
                        _targetParty = ConversationSentence.SelectedRepeatObject as MobileParty;
                    }
                    );

                _starter.AddPlayerLine(
                    "player_select_new_target_quit",
                    "player_select_new_target",
                    "lord_pretalk",
                    "{=D33fIGQe}Never mind.", null, null);
#endregion

#region Select New Chase Down Condition
                _starter.AddDialogLine(
                    "army_member_ask_chase_down_how_far",
                    "army_member_ask_chase_down_how_far",
                    "player_set_new_lord_chase_option",
                    "{=6DuwsVceki}And for how long should {SELECTED_PARTIES_TEXT} chase {TARGET_PARTY}?",
                    delegate ()
                    {
                        if (_targetParty.Army != null && _targetParty.Army.LeaderParty == _targetParty)
                            MBTextManager.SetTextVariable("TARGET_PARTY", _targetParty.Army.Name);
                        else
                            MBTextManager.SetTextVariable("TARGET_PARTY", _targetParty.Name);
                        return true;
                    },
                    null);

                _starter.AddPlayerLine(
                    "player_set_new_chase_while_visible",
                    "player_set_new_lord_chase_option",
                    "army_member_send_out_new_orders",
                    "{=iLg2YiqwXN}Tell them to give chase until they are out of sight. Tell them that if they haven't caught {TARGET_PARTY} at that point, then they should return.",
                    null,
                    delegate ()
                    {
                        _order = AdvancedPartyOrder.ChaseWhileVisible;
                    });

                _starter.AddPlayerLine(
                    "player_set_new_chase_always",
                    "player_set_new_lord_chase_option",
                    "army_member_send_out_new_orders",
                    "{=dOEfSBVb0X}Tell them to chase {TARGET_PARTY} to the ends of the earth.",
                    null,
                    delegate ()
                    {
                        _order = AdvancedPartyOrder.ChaseAlways;
                    });

                _starter.AddPlayerLine(
                    "player_set_new_lord_chase_option_quit",
                    "player_set_new_lord_chase_option",
                    "lord_pretalk",
                    "{=D33fIGQe}Never mind.", null, null);
#endregion

                _starter.AddDialogLine(
                    "army_member_send_out_new_orders",
                    "army_member_send_out_new_orders",
                    "hero_main_options",
                    "{=vcmYai61Wa}I shall send these new orders immediately.",
                    null,
                    delegate ()
                    {
                        foreach(MobileParty party in _reorderParties)
                        {
                            SendAdvancedPartyCampaignBehavior
                            .Instance.SetNewOrder(party, new AdvancedPartyTargeter()
                            {
                                Order = _order,
                                TargetParty = _targetParty
                            });
                        }
                    }
                    );
            }
#endregion

            public void Initialize()
            {
                InitializeSendAdvancedPartyDialog();
                InitializeReorderAdvancedPartiesDialog();
            }
        }

        public enum AdvancedPartyOrder
        {
            Invalid,
            ChaseAlways,
            ChaseWhileVisible,
            ReturnToArmy
        }

        public struct AdvancedPartyTargeter
        {
            public MobileParty? TargetParty;
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

    /// <summary>
    /// A corral for harmony patches.
    /// </summary>
    [HarmonyPatch]
    public class Patches
    {
        /// <summary>
        /// Forces an advanced party to keep its behavior target. So no fleeing and no chasing 
        /// after other parties.
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="newAiBehavior"></param>
        /// <param name="targetPartyFigure"></param>
        /// <param name="bestTargetPoint"></param>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MobileParty), "SetAiBehavior")]
        public static bool MobileParty_SetAiBehavior_Patch(MobileParty __instance, ref AiBehavior newAiBehavior, ref PartyBase targetPartyFigure, ref Vec2 bestTargetPoint)
        {
            if (SendAdvancedPartyCampaignBehavior.Instance.AdvancedPartiesTargets.ContainsKey(__instance))
            {
                var dict = SendAdvancedPartyCampaignBehavior.Instance.AdvancedPartiesTargets;
                if (dict[__instance].Order == SendAdvancedPartyCampaignBehavior.AdvancedPartyOrder.ChaseAlways
                    || dict[__instance].Order == SendAdvancedPartyCampaignBehavior.AdvancedPartyOrder.ChaseWhileVisible)
                {
                    newAiBehavior = __instance.DefaultBehavior;
                    if (__instance.TargetParty != null)
                    {
                        targetPartyFigure = __instance.TargetParty.Party;
                    }
                    bestTargetPoint = __instance.TargetPosition;
                }
            }
            return true;
        }
    }
}