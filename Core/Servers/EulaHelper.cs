namespace McServerLauncher.Core.Servers;

/// <summary>
/// Mojang's EULA (https://aka.ms/MinecraftEULA) requires an explicit, affirmative
/// acceptance before a Java-edition server will run. This only ever writes eula.txt
/// when the caller has already recorded the user's consent.
/// </summary>
public static class EulaHelper
{
    public static void Accept(string serverDirectory)
    {
        var path = Path.Combine(serverDirectory, "eula.txt");
        File.WriteAllText(path, "eula=true" + Environment.NewLine);
    }
}
