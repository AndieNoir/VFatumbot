// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Bot.Builder;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using System.Threading;

namespace VFatumbot
{
    public class AdapterWithErrorHandler : BotFrameworkHttpAdapter
    {
        protected readonly ConversationState  _conversationState;

        public AdapterWithErrorHandler(IConfiguration configuration, ILogger<BotFrameworkHttpAdapter> logger, ConversationState conversationState = null)
            : base(configuration, logger)
        {
            _conversationState = conversationState;

            OnTurnError = async (turnContext, exception) =>
            {
                // Log any leaked exception from the application.
                logger.LogError($"Exception caught : {exception.Message} {exception.StackTrace}");

                // Send a catch-all apology to the user.
                await turnContext.SendActivityAsync("Sorry, it looks like something went wrong. " + exception.Message + " " + exception.StackTrace);

                if (conversationState != null)
                {
                    try
                    {
                        // Delete the conversationState for the current conversation to prevent the
                        // bot from getting stuck in a error-loop caused by being in a bad state.
                        // ConversationState should be thought of as similar to "cookie-state" in a Web pages.
                        await conversationState.DeleteAsync(turnContext);
                    }
                    catch (Exception e)
                    {
                        logger.LogError($"Exception caught on attempting to Delete ConversationState : {e.Message}");
                    }
                }
            };
        }

        // Used as a callback to continue the dialog (i.e. prompt user for next action) after long tasks
        // like getting attractors etc have completed their work on a background thread
        public async Task ContinueDialogAsync(ITurnContext turnContext, Dialog dialog, CancellationToken cancellationToken)
        {
            var conversationStateAccessors = _conversationState.CreateProperty<DialogState>(nameof(DialogState));

            var dialogSet = new DialogSet(conversationStateAccessors);
            dialogSet.Add(dialog);

            var dialogContext = await dialogSet.CreateContextAsync(turnContext, cancellationToken);
            var results = await dialogContext.ContinueDialogAsync(cancellationToken);

            await dialogContext.BeginDialogAsync(dialog.Id, null, cancellationToken);
            await _conversationState.SaveChangesAsync(dialogContext.Context, false, cancellationToken);
        }
    }
}