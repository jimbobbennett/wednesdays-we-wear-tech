using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Device.Spi;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Iot.Device.Graphics;
using Iot.Device.Ws28xx;
using JimBobBennett.NeoPixelTickerTape;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Azure;
using Azure.AI.Language.Conversations;
using Octokit;
using System.Text.RegularExpressions;

namespace BeKindOMatic
{
    public enum Intent
    {
        Issues,
        Stars
    }

    class Program
    {
        static string SpeechSubscriptionKey = "";
        static string SpeechServiceRegion = "";

        static Uri ConversationEndpoint = new Uri("");
        static string ConversationKey = "";
        static string ConversationProjectName = "";
        static string ConversationDeploymentName = "";

        static string GitHubAPIKey = "";
        static string GitHubUserOrOrgName = "";

        const int PixelCount = 256;

        static Ws2812b _neoPixels;
        static Color _textColor = Color.FromArgb(Color.White.A, (byte)(Color.White.R / 8), (byte)(Color.White.G / 8), (byte)(Color.White.B / 8));

        static GitHubClient client = new GitHubClient(new ProductHeaderValue("GitHubStatusBracer"))
            {
                Credentials = new Credentials(GitHubAPIKey)
            };

        static Dictionary<string, string> repos = new Dictionary<string, string>();

        static string SplitCamelCase(string str)
        {
            return Regex.Replace(Regex.Replace(str, @"(\P{Ll})(\P{Ll}\p{Ll})", "$1 $2" ), @"(\p{Ll})(\P{Ll})", "$1 $2");
        }

        static async Task LoadGitHubRepos()
        {
            repos.Clear();

            var repositories = await client.Repository.GetAllForUser(GitHubUserOrOrgName);
            foreach (var repo in repositories)
            {
                var name = SplitCamelCase(repo.Name).Replace("IoT", "I O T").Replace("iot", "I O T").Replace('-', ' ').Replace('.', ' ').Replace("  ", " ").Replace("  ", " ");
                repos.Add(repo.Name, name);

                Console.WriteLine($"{repo.Name} - {name}");
            }
        }

        static Program()
        {
            SpiConnectionSettings settings = new(0, 0)
            {
                ClockFrequency = 2_400_000,
                Mode = SpiMode.Mode0,
                DataBitLength = 8
            };

            var spi = SpiDevice.Create(settings);
            _neoPixels = new Ws2812b(spi, PixelCount);
        }

        static void LightPixels(Color color)
        {
            var img = _neoPixels.Image;

            for (var i = 0; i < PixelCount; ++i)
            {
                img.SetPixel(i, 0, color);
            }

            _neoPixels.Update();
        }
                
        static string GetBestRepoNameMatch(string repoName)
        {
            var best = "";
            var bestDistance = int.MaxValue;

            foreach (var repo in repos)
            {
                var levenshteinDistance = Fastenshtein.Levenshtein.Distance(repoName, repo.Value);
                if (levenshteinDistance < bestDistance)
                {
                    best = repo.Key;
                    bestDistance = levenshteinDistance;
                }
            }

            return best;
        }

        static string GetRepoName(IReadOnlyList<ConversationEntity> entities)
        {
            var repoName = string.Join(" ", entities.OrderBy(e => e.Offset).Select(e => e.Text));
            repoName = repoName.Replace(" repo", "");

            return GetBestRepoNameMatch(repoName);
        }
        
        static Intent GetIntent(string topIntent)
        {
            return topIntent == "get issues" ? Intent.Issues : Intent.Stars;
        }

        async static Task<string> GetRepoDetails(Intent intent, string repoName)
        {
            var repo = await client.Repository.Get(GitHubUserOrOrgName, repoName);
            switch (intent)
            {
                case Intent.Issues:
                    return $"{repo.OpenIssuesCount} open issues";
                case Intent.Stars:
                    return $"{repo.StargazersCount} stars";
                default:
                    return "";
            }
        }

        async static Task<string> GetResponseForSpeech(ConversationAnalysisClient conversationClient, string speech)
        {
            var conversationsProject = new ConversationsProject(ConversationProjectName, ConversationDeploymentName);
            var response = await conversationClient.AnalyzeConversationAsync(speech, conversationsProject);

            var customConversationalTaskResult = response.Value;
            var conversationPrediction = (ConversationPrediction)customConversationalTaskResult.Prediction;

            var predictionIntent = conversationPrediction.TopIntent;
            var predictionEntities = conversationPrediction.Entities;

            var repoName = GetRepoName(conversationPrediction.Entities);
            var intent = GetIntent(conversationPrediction.TopIntent);

            Console.WriteLine($"Repo detected: {repoName}");
            Console.WriteLine($"Top intent: {intent}");

            return await GetRepoDetails(intent, repoName);
        }

        public static async Task Main(string[] args)
        {
            await LoadGitHubRepos();
       
            var speechConfig = SpeechConfig.FromSubscription(SpeechSubscriptionKey, SpeechServiceRegion);
            speechConfig.SpeechRecognitionLanguage = "en-US";
            
            using var audioConfig = AudioConfig.FromMicrophoneInput("plughw:1,0");
            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            var conversationCredential = new AzureKeyCredential(ConversationKey);
            var conversationClient = new ConversationAnalysisClient(ConversationEndpoint, conversationCredential);

            recognizer.Recognized += async (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    Console.WriteLine(e.Result.Text);
                    var response = await GetResponseForSpeech(conversationClient, e.Result.Text);
                    Console.WriteLine(response);
                    await TickerTapeWriter.ScrollText(_neoPixels, response, _textColor, 20);
                }
            };
        
            Console.WriteLine("Speak into your microphone.");
            await recognizer.StartContinuousRecognitionAsync();
        
            var stopRecognition = new TaskCompletionSource<int>();
            Task.WaitAny(new[] { stopRecognition.Task });
        }
    }
}