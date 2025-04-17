using BlinkChatBackend.Models;
using LMKit.Model;
using LMKit.TextGeneration;
using LMKit.TextGeneration.Chat;
using LMKit.TextGeneration.Sampling;
using System.Text;

namespace BlinkChatBackend.Services
{
    public class AIService: IAIService
    {
        static bool _isDownloading;
        private readonly LM _model;
        private readonly MultiTurnConversation _chat;
        public AIService()
        {
            LMKit.Licensing.LicenseManager.SetLicenseKey("");

            var modelLink = ModelCard.GetPredefinedModelCardByModelID("qwen2-vl:2b").ModelUri.ToString();
            var modelUri = new Uri(modelLink);
            _model = new LM(modelUri, downloadingProgress:ModelDownloadingProgress, loadingProgress:ModelLoadingProgress);

            _chat = new MultiTurnConversation(_model)
            {
                MaximumCompletionTokens = 1000,
                SamplingMode = new RandomSampling()
                {
                    Temperature = 0.8f
                },
                SystemPrompt = "You are a chatbot that always responds promptly and helpfully to user requests. You only respond to questions that are related to .Net, C#, .Net ecosystem or any .net framework or .net versions. You politely reject any questions that are not related to .Net"
            };
        }

        public async Task GetResponse(AIPrompt prompt, Stream responseStream)
        {
            _chat.AfterTokenSampling += async (sender, token) =>
            {
                if (token.TextChunk == "<|im_end|>")
                    return;
                var buffer = Encoding.UTF8.GetBytes(token.TextChunk);
                await responseStream.WriteAsync(buffer, 0, buffer.Length);
                await responseStream.FlushAsync();
            };
            await _chat.SubmitAsync(new Prompt(prompt.Question), new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
            
        }

        private static bool ModelDownloadingProgress(string path, long? contentLength, long bytesRead)
        {
            _isDownloading = true;
            if (contentLength.HasValue)
            {
                double progressPercentage = Math.Round((double)bytesRead / contentLength.Value * 100, 2);
                Console.Write($"\rDownloading model {progressPercentage:0.00}%");
            }
            else
            {
                Console.Write($"\rDownloading model {bytesRead} bytes");
            }

            return true;
        }

        private static bool ModelLoadingProgress(float progress)
        {
            if (_isDownloading)
            {
                Console.Clear();
                _isDownloading = false;
            }

            Console.Write($"\rLoading model {Math.Round(progress * 100)}%");

            return true;
        }
    }
}
