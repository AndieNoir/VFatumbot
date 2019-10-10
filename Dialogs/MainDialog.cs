﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using VFatumbot.BotLogic;

namespace VFatumbot
{
    public class MainDialog : ComponentDialog
    {
        protected readonly ILogger _logger;
        protected readonly UserState _userState;

        protected UserProfile UserProfile;

        public BotState _conversationState;

        public MainDialog(UserState userState, ILogger<MainDialog> logger) : base(nameof(MainDialog))
        {
            _logger = logger;
            _userState = userState;

            // Define the main dialog and its related components.
            AddDialog(new ScanDialog(userState, this, logger));
            AddDialog(new ChoicePrompt(nameof(ChoicePrompt)));
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
            {
                ChoiceActionStepAsync,
                PerformActionStepAsync,
            }));

            // The initial child Dialog to run.
            InitialDialogId = nameof(WaterfallDialog);
        }

        // Used as a callback to continue the dialog (i.e. prompt user for next action) after long tasks
        // like getting attractors etc have completed their work on a background thread
        public async Task ContinueDialog(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            var conversationStateAccessors = _conversationState.CreateProperty<DialogState>(nameof(DialogState));

            var dialogSet = new DialogSet(conversationStateAccessors);
            dialogSet.Add(this);

            var dialogContext = await dialogSet.CreateContextAsync(turnContext, cancellationToken);
            var results = await dialogContext.ContinueDialogAsync(cancellationToken);

            await dialogContext.BeginDialogAsync(Id, null, cancellationToken);
            await _conversationState.SaveChangesAsync(dialogContext.Context, false, cancellationToken);
        }

        // 1. Prompts the user if the user is not in the middle of a dialog.
        // 2. Re-prompts the user when an invalid input is received.
        public async Task<DialogTurnResult> ChoiceActionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            _logger.LogInformation("MainDialog.ChoiceActionStepAsync");

            var userStateAccessors = _userState.CreateProperty<UserProfile>(nameof(UserProfile));
            UserProfile = await userStateAccessors.GetAsync(stepContext.Context, () => new UserProfile());

            // Create the PromptOptions which contain the prompt and re-prompt messages.
            // PromptOptions also contains the list of choices available to the user.
            var options = new PromptOptions()
            {
                Prompt = MessageFactory.Text("What would you like to get/check?"),
                RetryPrompt = MessageFactory.Text("That is not a valid action. What would you like to get/check?"),
                Choices = GetActionChoices(),
            };

            // Prompt the user with the configured PromptOptions.
            return await stepContext.PromptAsync(nameof(ChoicePrompt), options, cancellationToken);
        }

        // Send a response to the user based on their choice.
        // This method is only called when a valid prompt response is parsed from the user's response to the ChoicePrompt.
        private async Task<DialogTurnResult> PerformActionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            _logger.LogInformation("MainDialog.PerformActionStepAsync");

            // Cards are sent as Attachments in the Bot Framework.
            // So we need to create a list of attachments for the reply activity.
            var attachments = new List<Attachment>();
            
            // Reply to the activity we received with an activity.
            var reply = MessageFactory.Attachment(attachments);

            var actionHandler = new ActionHandler();

            // Handle the chosen action
            switch (((FoundChoice)stepContext.Result).Value)
            {
                case "Attractor":
                    await actionHandler.AttractionActionAsync(stepContext, UserProfile, cancellationToken, this);
                    break;
                case "Void":
                    await actionHandler.VoidActionAsync(stepContext, UserProfile, cancellationToken, this);
                    break;
                case "Anomaly":
                    await actionHandler.AnomalyActionAsync(stepContext, UserProfile, cancellationToken, this);
                    break;
                case "Scan":
                    return await stepContext.BeginDialogAsync(nameof(ScanDialog), this, cancellationToken);
                case "My Location":
                    // TODO: we shouldn't need this location-is-set check here because it's checked higher up in VFatumbot.cs but the location wasn't been set sometimes so just for debugging now...
                    if (!UserProfile.IsLocationSet)
                    {
                        await stepContext.Context.SendActivityAsync(MessageFactory.Text(Consts.NO_LOCATION_SET_MSG), cancellationToken);
                        await stepContext.RepromptDialogAsync(cancellationToken);
                    }
                    // END TODO

                    await actionHandler.LocationActionAsync(stepContext, UserProfile, cancellationToken);
                    break;
                case "Settings":
                    // TODO: do settings stuff
                    break;

                //case "Radius":
                //    await actionHandler.RadiusActionAsync(stepContext, UserProfile, cancellationToken);
                //    break;

                //case "Quantum":
                //    await actionHandler.QuantumActionAsync(stepContext, UserProfile, cancellationToken);
                //    break;
                //case "Quantum Time":
                //    await actionHandler.QuantumActionAsync(stepContext, UserProfile, cancellationToken, true);
                //    break;
                //case "Psuedo":
                //    await actionHandler.PsuedoActionAsync(stepContext, UserProfile, cancellationToken);
                //    break;
                //case "Pair":
                //    await actionHandler.PairActionAsync(stepContext, UserProfile, cancellationToken, this);
                //    break;
                //case "Scan Attractor":
                //    await actionHandler.AttractionActionAsync(stepContext, UserProfile, cancellationToken, this, true);
                //    break;
                //case "Scan Void":
                //    await actionHandler.VoidActionAsync(stepContext, UserProfile, cancellationToken, this, true);
                //    break;
                //case "Scan Anomaly":
                //    await actionHandler.AnomalyActionAsync(stepContext, UserProfile, cancellationToken, this, true);
                //    break;
                //case "Scan Pair":
                //    await actionHandler.PairActionAsync(stepContext, UserProfile, cancellationToken, this, true);
                //    break;
                //case "Point":
                //    await actionHandler.PointActionAsync(stepContext, UserProfile, cancellationToken, this);
                //    break;
                //case "Water On":
                //case "Water Off":
                //    await actionHandler.ToggleWaterSkip(stepContext, UserProfile, cancellationToken);
                //    await actionHandler.SaveActionAsync(stepContext, _userState, UserProfile, cancellationToken);
                //    break;
                //case "Save":
                //    await actionHandler.SaveActionAsync(stepContext, _userState, UserProfile, cancellationToken);
                //    break;
            }

            // Send the reply
            await stepContext.Context.SendActivityAsync(reply, cancellationToken);

            return await stepContext.EndDialogAsync();
        }

        private IList<Choice> GetActionChoices()
        {
            var actionOptions = new List<Choice>()
            {
                new Choice() {
                    Value = "Attractor",
                    Synonyms = new List<string>()
                                    {
                                        "attractor",
                                        "getattractor",
                                        "/getattractor",
                                    }
                },
                new Choice() {
                    Value = "Void",
                    Synonyms = new List<string>()
                                    {
                                        "void",
                                        "getvoid",
                                        "/getvoid",
                                        "Repeller",
                                        "repeller",
                                        "getrepeller",
                                        "/getrepeller"
                                    }
                },
                new Choice() {
                    Value = "Anomaly",
                    Synonyms = new List<string>()
                                    {
                                        "anomaly",
                                        "getanomaly",
                                        "/getanomaly",
                                        "ida",
                                        "getida",
                                        "/getida",
                                    }
                },
                new Choice() {
                    Value = "Scan",
                    Synonyms = new List<string>()
                                    {
                                        "scan",
                                        "/scan",
                                    }
                },
                new Choice() {
                    Value = "My Location",
                    Synonyms = new List<string>()
                                    {
                                        "My Location",
                                        "My location",
                                        "my location",
                                        "location",
                                        "setlocation",
                                        "/setlocation",
                                    }
                },
                 new Choice() {
                    Value = "Settings",
                    Synonyms = new List<string>()
                                    {
                                        "settings",
                                    }
                },

                //new Choice() {
                //    Value = "Radius",
                //    Synonyms = new List<string>()
                //                    {
                //                        "radius",
                //                        "setradius",
                //                        "/setradius",
                //                    }
                //},
                //new Choice() {
                //    Value = "Quantum",
                //    Synonyms = new List<string>()
                //                    {
                //                        "quantum",
                //                        "getquantum",
                //                        "/getquantum",
                //                    }
                //},
                //new Choice() {
                //    Value = "Quantum Time",
                //    Synonyms = new List<string>()
                //                    {
                //                        "quantumtime",
                //                        "getquantumtime",
                //                        "qtime",
                //                        "/getqtime",
                //                    }
                //},
                //new Choice() {
                //    Value = "Psuedo",
                //    Synonyms = new List<string>()
                //                    {
                //                        "psuedo",
                //                        "getpsuedo",
                //                        "/getpsuedo",
                //                    }
                //},
                //new Choice() {
                //    Value = "Pair",
                //    Synonyms = new List<string>()
                //                    {
                //                        "pair",
                //                        "getpair",
                //                        "/getpair",
                //                    }
                //},
                //new Choice() {
                //    Value = "Point",
                //    Synonyms = new List<string>()
                //                    {
                //                        "point",
                //                        "getpoint",
                //                        "/getpoint",
                //                    }
                //},
                //new Choice() {
                //    Value = "Water " + (UserProfile.IsIncludeWaterPoints ? "Off" : "On"),
                //    Synonyms = new List<string>()
                //                    {
                //                        "water",
                //                    }
                //},
                //new Choice() {
                //    Value = "Save",
                //    Synonyms = new List<string>()
                //                    {
                //                        "save",
                //                        "setdefault",
                //                    }
                //},
            };

            return actionOptions;
        }
    }
}
