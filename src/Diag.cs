using System;
using System.Collections.Generic;
using System.Text;

namespace MicPin
{
    /// <summary>
    /// Console diagnostics: lists endpoints, shows the current defaults and can
    /// switch the default input so the Core Audio path can be verified.
    /// </summary>
    internal static class DiagProgram
    {
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (Exception)
            {
            }

            AudioSystem audio;
            try
            {
                audio = new AudioSystem();
            }
            catch (Exception error)
            {
                Console.WriteLine("Failed to create the audio client: " + error.Message);
                return 2;
            }

            List<AudioInput> devices = audio.GetInputs();
            Console.WriteLine("Capture endpoints: " + devices.Count);
            foreach (AudioInput device in devices)
            {
                Console.WriteLine("  [" + device.Kind + "] " + device.Name);
                Console.WriteLine("      id   = " + device.Id);
                Console.WriteLine("      enum = " + device.Enumerator);
            }

            PrintDefault(audio, AudioConstants.RoleConsole, "console");
            PrintDefault(audio, AudioConstants.RoleMultimedia, "multimedia");
            PrintDefault(audio, AudioConstants.RoleCommunications, "communications");

            AudioInput automatic = AudioSystem.PickAutomatic(devices, null);
            Console.WriteLine("Automatic pick: " + (automatic != null ? automatic.Name : "(none)"));
            Console.WriteLine("PolicyConfig available: " + audio.CanSwitchDefault);

            if (args.Length >= 2 && string.Equals(args[0], "--set", StringComparison.OrdinalIgnoreCase))
            {
                string target = ResolveTarget(args[1], devices);
                if (target == null)
                {
                    Console.WriteLine("No matching device for: " + args[1]);
                    return 3;
                }
                bool applied = audio.SetDefaultInput(target);
                Console.WriteLine("Set default to " + target + " -> " + applied);
                PrintDefault(audio, AudioConstants.RoleConsole, "console");
                PrintDefault(audio, AudioConstants.RoleCommunications, "communications");
                audio.Dispose();
                return applied ? 0 : 4;
            }

            // Harmless round-trip: re-assert the device that is already default.
            string current = audio.GetDefaultInputId(AudioConstants.RoleConsole);
            if (!string.IsNullOrEmpty(current))
            {
                Console.WriteLine();
                Console.WriteLine("Round-trip SetDefaultEndpoint on the current default:");
                int hresult = audio.SetDefaultInput(current) ? 0 : 1;
                Console.WriteLine("  result = " + (hresult == 0 ? "OK" : "FAILED"));
            }

            audio.Dispose();
            return 0;
        }

        private static void PrintDefault(AudioSystem audio, int role, string label)
        {
            string id = audio.GetDefaultInputId(role);
            string name = audio.GetKnownName(id);
            Console.WriteLine("Default (" + label + "): " + (name ?? "(unknown)") + "  " + (id ?? string.Empty));
        }

        private static string ResolveTarget(string argument, List<AudioInput> devices)
        {
            foreach (AudioInput device in devices)
            {
                if (device.Id == argument)
                {
                    return device.Id;
                }
            }

            foreach (AudioInput device in devices)
            {
                if (device.Name.IndexOf(argument, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return device.Id;
                }
            }
            return null;
        }
    }
}
