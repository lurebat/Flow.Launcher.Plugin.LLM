using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenAI;

namespace Flow.Launcher.Plugin.LLM
{
    /// <summary>
    /// Plugin for generating text using an LLM via OpenAI API.
    /// </summary>
    public class LLM : IAsyncPlugin, IResultUpdated
    {
        private PluginInitContext _context;

        private OpenAIClient _api;

        // Constants for image paths
        private const string AppIconPath = "images/app.png";

        // TODO - move to settings
        private readonly string _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        private readonly string _apiBase = Environment.GetEnvironmentVariable("OPENAI_API_BASE") ?? "https://api.openai.com/v1";

        private readonly string _model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4.1";

        /// <inheritdoc />
        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            // Check for missing API key
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                return MakeResult(
                    "Missing API Key",
                    "Please set the OPENAI_API_KEY environment variable."
                );
            }

            // API client might not be initialized if API key was missing during InitAsync
            if (_api == null)
            {
                return MakeResult(
                    "API Client Not Initialized",
                    "Please ensure the OPENAI_API_KEY and OPENAI_API_BASE environment variables are set."
                );
            }

            return MakeResult(
                "Generate Text",
                $"Use '{_model}' to generate text for: {query.Search}",
                asyncAction: async _ => await ProcessQueryAsync(query, token).ConfigureAwait(false)
            );
        }

        private async Task<bool> ProcessQueryAsync(Query query, CancellationToken token)
        {
            void UpdateResult(string title, string subtitle, Func<ActionContext, bool> action = null, string iconPath = AppIconPath)
            {
                ResultsUpdated?.Invoke(
                    this,
                    new ResultUpdatedEventArgs
                    {
                        Query = query,
                        Token = token,
                        Results = MakeResult(title, subtitle, action, iconPath: iconPath)
                    }
                );
            }

            UpdateResult("...", "Generating text...");

            var text = new StringBuilder();

            try
            {
                var chatClient = _api.GetChatClient(_model);
                var result = chatClient.CompleteChatStreamingAsync(query.Search);

                await foreach (var chunk in result.ConfigureAwait(false))
                {
                    if (token.IsCancellationRequested)
                    {
                        UpdateResult("Cancelled", "Text generation was cancelled.");

                        return false;
                    }

                    foreach (var update in chunk.ContentUpdate)
                    {
                        text.Append(update.Text);
                        UpdateResult(text.ToString(), "Generating text (streaming)...");
                    }
                }

                UpdateResult(
                    text.ToString(),
                    "Generated Text (Click to copy)",
                    _ =>
                    {
                        _context.API.CopyToClipboard(text.ToString());

                        return true;
                    }
                );
            }
            catch (Exception ex)
            {
                UpdateResult("Error generating text", ex.Message);
            }

            return false;
        }

        private static List<Result> MakeResult(
            string title,
            string subtitle,
            Func<ActionContext, bool> action = null,
            Func<ActionContext, ValueTask<bool>> asyncAction = null,
            string iconPath = AppIconPath)
        {
            return new List<Result>
            {
                new Result
                {
                    Title = title,
                    SubTitle = subtitle,
                    Action = action,
                    AsyncAction = asyncAction,
                    IcoPath = iconPath,
                }
            };
        }

        /// <inheritdoc />
        public Task InitAsync(PluginInitContext context)
        {
            _context = context;

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                // Don't initialize API client if API key is missing
                return Task.CompletedTask;
            }

            _api = new OpenAIClient(
                new ApiKeyCredential(_apiKey),
                new OpenAIClientOptions()
                {
                    Endpoint = new Uri(_apiBase),
                }
            );

            return Task.CompletedTask;
        }

        public event ResultUpdatedEventHandler ResultsUpdated;
    }
}