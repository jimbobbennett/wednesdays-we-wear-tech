using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Device.Spi;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Azure;
using Azure.AI.TextAnalytics;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Iot.Device.Graphics;
using Iot.Device.Ws28xx;

namespace BeKindOMatic
{
    class Program
    {
        const int PixelCount = 20;

        static Ws2812b _neoPixels;

        static string YourSubscriptionKey = "";
        static string YourServiceRegion = "";

        static readonly AzureKeyCredential credentials = new AzureKeyCredential("");
        static readonly Uri endpoint = new Uri("");

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

        public static async Task Main(string[] args)
        {
            var speechConfig = SpeechConfig.FromSubscription(YourSubscriptionKey, YourServiceRegion);        
            speechConfig.SpeechRecognitionLanguage = "en-US";
            
            using var audioConfig = AudioConfig.FromMicrophoneInput("plughw:1,0");
            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            var textAnalyticsClient = new TextAnalyticsClient(endpoint, credentials);

            recognizer.Recognized += (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    Console.WriteLine(e.Result.Text);

                    var _ = Task.Run(() => {
                        Console.WriteLine("Determining sentiment...");

                        DocumentSentiment documentSentiment = textAnalyticsClient.AnalyzeSentiment(e.Result.Text);

                        Console.WriteLine($"Document sentiment: {documentSentiment.Sentiment}");
                        Console.WriteLine($"Negative          : {documentSentiment.ConfidenceScores.Negative}");
                        Console.WriteLine($"Positive          : {documentSentiment.ConfidenceScores.Positive}");

                        Color color = Color.Black;

                        if (documentSentiment.ConfidenceScores.Negative > documentSentiment.ConfidenceScores.Positive)
                        {
                            color = Color.FromArgb(255, (int)(255 * documentSentiment.ConfidenceScores.Negative), 0, 0);
                        }
                        else
                        {
                            color = Color.FromArgb(255, 0, (int)(255 * documentSentiment.ConfidenceScores.Positive), 0);
                        }

                        LightPixels(color);
                    });
                }
            };

            Console.WriteLine("Speak into your microphone.");
            await recognizer.StartContinuousRecognitionAsync();

            var _ = Task.Run(async () => {
                while(true)
                {
                    await Task.Delay(100);
                }
            });

            var stopRecognition = new TaskCompletionSource<int>();
            Task.WaitAny(new[] { stopRecognition.Task });
        }
    }
}
