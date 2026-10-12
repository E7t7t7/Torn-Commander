namespace TornCommander;

public sealed partial class MainForm
{
    // Older installers shipped two non-neutral example labels. Never assume a name
    // belongs to the player using this computer, and never alter local profiles
    // without explicit approval. The original profile IDs are preserved so saved
    // cookies and Windows Credential Manager entries still resolve correctly.
    private void ReviewLegacyExampleProfileLabels()
    {
        if (_settings.LegacyExampleLabelsReviewed) return;
        var main = _settings.Profiles.FirstOrDefault(p =>
            p.Id.Equals("main", StringComparison.OrdinalIgnoreCase));
        var secondary = _settings.Profiles.FirstOrDefault(p =>
            p.Id.Equals("wife", StringComparison.OrdinalIgnoreCase));

        // Fresh, neutral installs don't need a migration prompt.
        if (main is null || secondary is null ||
            (main.Name.Equals("Profile 1", StringComparison.OrdinalIgnoreCase) &&
             secondary.Name.Equals("Profile 2", StringComparison.OrdinalIgnoreCase)))
        {
            _settings.LegacyExampleLabelsReviewed = true;
            Storage.SaveSettings(_settings);
            return;
        }

        var answer = MessageBox.Show(this,
            "Earlier Commander releases shipped example names as default profile labels.\\n\\n" +
            "Would you like to rename the two original profile labels to Profile 1 and Profile 2?\\n\\n" +
            "Only the display labels change. Login sessions, API keys, profile IDs and account data are preserved.\\n\\n" +
            "Choose No to keep your current names.",
            "Local profile display names",
            MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);

        if (answer == DialogResult.Yes)
        {
            main.Name = "Profile 1";
            secondary.Name = "Profile 2";
            RefreshProfileSelector();
            RefreshLossProfileCombos();
            Text = $"Torn Commander v0.6.8 — {CurrentProfile.Name}";
        }
        _settings.LegacyExampleLabelsReviewed = true;
        Storage.SaveSettings(_settings);
    }
}
