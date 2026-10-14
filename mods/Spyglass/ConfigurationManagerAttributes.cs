// Standard helper class read by name by BepInEx Configuration Manager (including the Valheim build).
// Not a dependency: if Configuration Manager isn't installed, this is simply ignored.
#pragma warning disable 0169, 0414, 0649
internal sealed class ConfigurationManagerAttributes
{
    /// <summary>Show this setting only when "Advanced settings" is ticked.</summary>
    public bool? IsAdvanced;
}
