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
using Microsoft.CognitiveServices.Speech.Translation;

namespace Subtitles
{
    class Program
    {
        static string YourSubscriptionKey = "";
        static string YourServiceRegion = "";

        const int PixelCount = 256;

        static Ws2812b _neoPixels;
        static Color _textColor = Color.FromArgb(Color.White.A, (byte)(Color.White.R / 8), (byte)(Color.White.G / 8), (byte)(Color.White.B / 8));

        static Program()
        {
            SpiConnectionSettings settings = new(0, 0)
            {
                ClockFrequency = 2_400_000,
                Mode = SpiMode.Mode0,
                DataBitLength = 8
            };

            SpiDevice spi = SpiDevice.Create(settings);
            _neoPixels = new Ws2812b(spi, PixelCount);
        }

        static ConcurrentQueue<string> _subtitlesQueue = new ConcurrentQueue<string>();

        public static async Task Main(string[] args)
        {
            var speechConfig = SpeechTranslationConfig.FromSubscription(YourSubscriptionKey, YourServiceRegion);
            speechConfig.SpeechRecognitionLanguage = "en-US";
            speechConfig.AddTargetLanguage("fr");
            
            using var audioConfig = AudioConfig.FromMicrophoneInput("plughw:1,0");
             using var recognizer = new TranslationRecognizer(speechConfig, audioConfig);

            recognizer.Recognized += (s, e) =>
            {
                if (e.Result.Reason == ResultReason.TranslatedSpeech)
                {
                    var translated = e.Result.Translations["fr"];
                    _subtitlesQueue.Enqueue(translated);
                }
            };

             var _ = Task.Run(async () => {
                while(true)
                {
                    string? subtitle;
                    if (_subtitlesQueue.TryDequeue(out subtitle) && subtitle != null)
                    {
                        await TickerTapeWriter.ScrollText(_neoPixels, subtitle, _textColor, 10);
                    }

                    await Task.Delay(100);
                }
            });
        
            Console.WriteLine("Speak into your microphone.");
            await recognizer.StartContinuousRecognitionAsync();
        
            var stopRecognition = new TaskCompletionSource<int>();
            Task.WaitAny(new[] { stopRecognition.Task });
        }
    }
}