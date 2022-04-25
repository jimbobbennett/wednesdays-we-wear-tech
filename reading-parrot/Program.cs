using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using MMALSharp;
using MMALSharp.Common;
using MMALSharp.Components;
using MMALSharp.Handlers;
using MMALSharp.Ports;
using MMALSharp.Ports.Outputs;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using System.Device.Gpio;

class Program 
{
    static string YourSubscriptionKey = "";
    static string YourServiceRegion = "";

    static string ComputerVisionSubscriptionKey = "";
    static string ComputerVisionEndpoint = "";

    async static Task<string> TakePicture(MMALCamera cam)
    {
        using (var imgCaptureHandler = new ImageStreamCaptureHandler(".", "jpg"))
        {
            await cam.TakePicture(imgCaptureHandler, MMALEncoding.JPEG, MMALEncoding.I420);
            var fileName = $"{imgCaptureHandler.GetFilename()}.jpg";

            Console.WriteLine($"Captured {fileName}");

            return fileName;
        }
    }

    static Guid GetOperationId(ReadInStreamHeaders headers)
    {
        var operationLocation = headers.OperationLocation;
        var operationId = operationLocation.Substring(operationLocation.Length - 36);
        return Guid.Parse(operationId);
    }

    async static Task<string> ReadTextInImage(ComputerVisionClient client, string fileName)
    {
        using (var fileStream = File.Open(fileName, FileMode.Open))
        {
            var textHeaders = await client.ReadInStreamAsync(fileStream);
            var operationId = GetOperationId(textHeaders);

            var results = await client.GetReadResultAsync(operationId);

            while (results.Status == OperationStatusCodes.Running || results.Status == OperationStatusCodes.NotStarted)
            {
                Thread.Sleep(100);
                results = await client.GetReadResultAsync(operationId);
            }

            var readText = "";

            foreach (var page in results.AnalyzeResult.ReadResults)
            {
                foreach (var line in page.Lines)
                {
                    readText += line.Text + "\n";
                }
            }

            return readText;
        }
    }

    async static Task CaptureAndProcessFile(MMALCamera cam, ComputerVisionClient computerVisionClient, SpeechSynthesizer speechSynthesizer)
    {
        Console.WriteLine("Taking picture...");

        var fileName = await TakePicture(cam);
        
        Console.WriteLine("Detecting text...");

        var toSpeak = await ReadTextInImage(computerVisionClient, fileName);

        Console.WriteLine("Text detected:");
        Console.WriteLine(toSpeak);

        File.Delete(fileName);

        Console.WriteLine("Speaking text...");
        var speechSynthesisResult = await speechSynthesizer.SpeakTextAsync(toSpeak);
        Console.WriteLine("Done!");
    }

    async static Task Main(string[] args)
    {
        var cam = MMALCamera.Instance;

        var speechConfig = SpeechConfig.FromSubscription(YourSubscriptionKey, YourServiceRegion);
        speechConfig.SpeechSynthesisVoiceName = "en-US-JennyNeural";
        var speechSynthesizer = new SpeechSynthesizer(speechConfig);

        var credentials = new ApiKeyServiceClientCredentials(ComputerVisionSubscriptionKey);
        var computerVisionClient = new ComputerVisionClient(credentials) { Endpoint = ComputerVisionEndpoint };

        int pin = 17;
        using GpioController controller = new();
        controller.OpenPin(pin, PinMode.InputPullDown);

        Console.WriteLine("Press button to read");

        while(true)
        {
            if (controller.Read(pin) == PinValue.High)
            {
                await CaptureAndProcessFile(cam, computerVisionClient, speechSynthesizer);
            }
        }

        cam.Cleanup();
    }
}