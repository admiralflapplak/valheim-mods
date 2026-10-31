// Standard helper class read by name by BepInEx Configuration Manager (including the Valheim build).
// Not a dependency: if Configuration Manager isn't installed, this is simply ignored.
// Only the fields this mod uses are included; Configuration Manager tolerates missing ones.
#pragma warning disable 0169, 0414, 0649
internal sealed class ConfigurationManagerAttributes
{
    /// <summary>Show this setting only when "Advanced settings" is ticked.</summary>
    public bool? IsAdvanced;

    /// <summary>Higher numbers are shown first within a category.</summary>
    public int? Order;

    /// <summary>Replace the default editor with custom IMGUI (used here for buttons).</summary>
    public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;

    /// <summary>Hide the "Reset" button next to the setting.</summary>
    public bool? HideDefaultButton;

    /// <summary>Hide the setting's name (for notes that span the whole row).</summary>
    public bool? HideSettingName;

    public bool? ReadOnly;
    public bool? Browsable;
    public string Category;
    public string DispName;
    public string Description;
}
