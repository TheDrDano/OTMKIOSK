using Otm.Kiosk.Shared.Security;
using Otm.Kiosk.Shared.Storage;

namespace Otm.Kiosk.RecoveryTool;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("OTM Kiosk Recovery Tool");
        Console.WriteLine("This tool disables enforcement locally and can reset the admin PIN.");
        Console.WriteLine();

        try
        {
            var store = new SqliteKioskStore();
            var policy = store.LoadOrCreate();

            if (args.Contains("--reset-pin", StringComparer.OrdinalIgnoreCase))
            {
                var newPin = ReadSecret("New admin PIN: ");
                if (newPin.Length < 6)
                {
                    Console.Error.WriteLine("PIN must be at least 6 characters.");
                    return 2;
                }

                policy.Admin.PasswordHash = PasswordHasher.Hash(newPin);
                policy.Admin.RequirePasswordChange = false;
            }

            policy.Enforcement.Enabled = false;
            store.Save(policy);

            var logs = store;
            logs.Append(new()
            {
                Level = "Warning",
                EventType = "RecoveryReset",
                Message = "Recovery tool disabled kiosk enforcement locally.",
                UserName = Environment.UserName
            });

            Console.WriteLine("Kiosk enforcement is now disabled in the local policy.");
            Console.WriteLine($"Database path: {KioskPaths.DatabasePath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string(chars.ToArray());
            }

            if (key.Key == ConsoleKey.Backspace && chars.Count > 0)
            {
                chars.RemoveAt(chars.Count - 1);
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                chars.Add(key.KeyChar);
            }
        }
    }
}
