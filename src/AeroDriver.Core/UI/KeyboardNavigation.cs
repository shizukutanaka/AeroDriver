using System.Runtime.Versioning;

namespace AeroDriver.Core.UI;

/// <summary>
/// Keyboard navigation utilities for accessibility
/// </summary>
[SupportedOSPlatform("windows")]
public static class KeyboardNavigation
{
    /// <summary>
    /// Common keyboard shortcuts
    /// </summary>
    public static class Shortcuts
    {
        public const ConsoleKey Refresh = ConsoleKey.F5;
        public const ConsoleKey Help = ConsoleKey.F1;
        public const ConsoleKey Search = ConsoleKey.F;      // Ctrl+F
        public const ConsoleKey Quit = ConsoleKey.Q;        // Ctrl+Q or Alt+F4
        public const ConsoleKey Navigate = ConsoleKey.Tab;
        public const ConsoleKey Back = ConsoleKey.Escape;
    }

    /// <summary>
    /// Navigate menu items with arrow keys
    /// </summary>
    public static int NavigateMenu(string[] menuItems, int currentIndex = 0, string prompt = "Use ↑↓ to navigate, Enter to select, Esc to cancel")
    {
        Console.CursorVisible = false;
        var selectedIndex = currentIndex;

        try
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine(prompt);
                Console.WriteLine();

                for (int i = 0; i < menuItems.Length; i++)
                {
                    if (i == selectedIndex)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkBlue;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine($"  → {menuItems[i]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine($"    {menuItems[i]}");
                    }
                }

                var key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        selectedIndex = selectedIndex > 0 ? selectedIndex - 1 : menuItems.Length - 1;
                        break;

                    case ConsoleKey.DownArrow:
                        selectedIndex = selectedIndex < menuItems.Length - 1 ? selectedIndex + 1 : 0;
                        break;

                    case ConsoleKey.Home:
                        selectedIndex = 0;
                        break;

                    case ConsoleKey.End:
                        selectedIndex = menuItems.Length - 1;
                        break;

                    case ConsoleKey.Enter:
                        return selectedIndex;

                    case ConsoleKey.Escape:
                        return -1;

                    case ConsoleKey.Tab:
                        selectedIndex = selectedIndex < menuItems.Length - 1 ? selectedIndex + 1 : 0;
                        break;
                }
            }
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    /// <summary>
    /// Multi-select menu with checkboxes
    /// </summary>
    public static List<int> MultiSelectMenu(string[] menuItems, string prompt = "Use ↑↓ to navigate, Space to toggle, Enter to confirm")
    {
        Console.CursorVisible = false;
        var selectedIndex = 0;
        var checkedItems = new HashSet<int>();

        try
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine(prompt);
                Console.WriteLine();

                for (int i = 0; i < menuItems.Length; i++)
                {
                    var checkbox = checkedItems.Contains(i) ? "☑" : "☐";
                    var prefix = i == selectedIndex ? "→ " : "  ";

                    if (i == selectedIndex)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkBlue;
                        Console.ForegroundColor = ConsoleColor.White;
                    }

                    Console.WriteLine($"{prefix}{checkbox} {menuItems[i]}");
                    Console.ResetColor();
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"Selected: {checkedItems.Count} items");
                Console.ResetColor();

                var key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        selectedIndex = selectedIndex > 0 ? selectedIndex - 1 : menuItems.Length - 1;
                        break;

                    case ConsoleKey.DownArrow:
                        selectedIndex = selectedIndex < menuItems.Length - 1 ? selectedIndex + 1 : 0;
                        break;

                    case ConsoleKey.Spacebar:
                        if (checkedItems.Contains(selectedIndex))
                            checkedItems.Remove(selectedIndex);
                        else
                            checkedItems.Add(selectedIndex);
                        break;

                    case ConsoleKey.Enter:
                        return checkedItems.ToList();

                    case ConsoleKey.Escape:
                        return new List<int>();

                    case ConsoleKey.A when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                        // Ctrl+A: Select all
                        for (int i = 0; i < menuItems.Length; i++)
                            checkedItems.Add(i);
                        break;
                }
            }
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    /// <summary>
    /// Yes/No confirmation with keyboard
    /// </summary>
    public static bool Confirm(string message, bool defaultYes = true)
    {
        Console.WriteLine();
        Console.Write($"{message} ");

        if (defaultYes)
        {
            Console.Write("[Y/n]: ");
        }
        else
        {
            Console.Write("[y/N]: ");
        }

        var key = Console.ReadKey(true);
        Console.WriteLine();

        return key.Key switch
        {
            ConsoleKey.Y => true,
            ConsoleKey.N => false,
            ConsoleKey.Enter => defaultYes,
            _ => defaultYes
        };
    }

    /// <summary>
    /// Wait for any key press with timeout
    /// </summary>
    public static bool WaitForKeyPress(string message = "Press any key to continue...", int timeoutSeconds = 0)
    {
        Console.WriteLine(message);

        if (timeoutSeconds <= 0)
        {
            Console.ReadKey(true);
            return true;
        }

        var startTime = DateTime.Now;
        while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
        {
            if (Console.KeyAvailable)
            {
                Console.ReadKey(true);
                return true;
            }
            Thread.Sleep(100);
        }

        return false;
    }
}
