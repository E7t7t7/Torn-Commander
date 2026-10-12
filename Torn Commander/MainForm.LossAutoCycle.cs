using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace TornCommander;

public sealed partial class MainForm
{
    private readonly object _webEnvironmentGate = new();
    private readonly Dictionary<string, Task<CoreWebView2Environment>> _webEnvironmentTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lossClaimGate = new();
    private readonly Dictionary<string, LossClaimLease> _lossClaims = new(StringComparer.OrdinalIgnoreCase);

    private LossAutoCycleWorker? _lossWorkerA;
    private LossAutoCycleWorker? _lossWorkerB;
    private Panel _lossWorkerHostA = null!;
    private Panel _lossWorkerHostB = null!;
    private NumericUpDown _lossMaxKits = null!;

    private sealed class LossClaimLease
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string OrderId { get; init; } = "";
        public string TargetId { get; init; } = "";
        public string Slot { get; init; } = "";
        public string ProfileId { get; init; } = "";
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }

    private Task<CoreWebView2Environment> GetWebEnvironmentAsync(string profileId)
    {
        var id = string.IsNullOrWhiteSpace(profileId) ? "main" : profileId;
        lock (_webEnvironmentGate)
        {
            if (_webEnvironmentTasks.TryGetValue(id, out var existing)) return existing;
            var created = CoreWebView2Environment.CreateAsync(null, Storage.BrowserDataPathFor(id));
            _webEnvironmentTasks[id] = created;
            return created;
        }
    }

    private void BuildLossAutoCycleSection(FlowLayoutPanel p)
    {
        p.Controls.Add(Separator());
        p.Controls.Add(Heading("AUTO CYCLE - INTEGRATED"));
        p.Controls.Add(Body("This ports the old Loss Desk attack → Pillow → visible-loss detection → First Aid Kit recovery → repeat state machine directly into Commander. No browser extension or reteaching is required. Account A and B use their isolated Commander browser profiles and a shared target lease so they cannot both hit the same buyer at once."));
        p.Controls.Add(Muted("IMPORTANT: this mode performs repeated Torn browser actions automatically. That can violate Torn's scripting rules and can risk the accounts. Use it only if you accept that risk. Cloudflare / verification screens, ambiguous controls, unexpected results, missing Pillow, or uncertain healing pause the worker instead of guessing."));

        var settingsRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        settingsRow.Controls.Add(new Label { Text = "Max First Aid Kits / loss:", Width = 155, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = TextMain });
        _lossMaxKits = Num(4, 1, 8);
        _lossMaxKits.Width = 70;
        settingsRow.Controls.Add(_lossMaxKits);
        settingsRow.Controls.Add(MakeButton("Initialize A", async (_, _) => await InitializeLossWorkerAsync(true), 92));
        settingsRow.Controls.Add(MakeButton("Initialize B", async (_, _) => await InitializeLossWorkerAsync(false), 92));
        p.Controls.Add(settingsRow);

        var startRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        startRow.Controls.Add(MakeButton("START AUTO A", async (_, _) => await StartLossAutoCycleAsync(true), 105));
        startRow.Controls.Add(MakeButton("START AUTO B", async (_, _) => await StartLossAutoCycleAsync(false), 105));
        startRow.Controls.Add(MakeButton("Resume A", async (_, _) => await ResumeLossWorkerAsync(true), 82));
        startRow.Controls.Add(MakeButton("Resume B", async (_, _) => await ResumeLossWorkerAsync(false), 82));
        p.Controls.Add(startRow);

        var stopRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        stopRow.Controls.Add(MakeButton("Pause A", async (_, _) => await PauseLossWorkerAsync(true), 78));
        stopRow.Controls.Add(MakeButton("Pause B", async (_, _) => await PauseLossWorkerAsync(false), 78));
        stopRow.Controls.Add(MakeButton("PAUSE BOTH", async (_, _) => await PauseBothLossWorkersAsync(), 98));
        stopRow.Controls.Add(MakeButton("RESUME BOTH", async (_, _) => await ResumeBothLossWorkersAsync(), 102));
        stopRow.Controls.Add(MakeButton("STOP A", async (_, _) => await StopLossWorkerAsync(true), 72));
        stopRow.Controls.Add(MakeButton("STOP B", async (_, _) => await StopLossWorkerAsync(false), 72));
        stopRow.Controls.Add(MakeButton("STOP BOTH", async (_, _) => await StopBothLossWorkersAsync(), 88));
        p.Controls.Add(stopRow);

        p.Controls.Add(Muted("START AUTO uses the selected Loss Desk order. For a shared order, A and B dynamically claim one loss at a time. Only one claim can hold the target lease, so the second account waits instead of attacking the same player simultaneously. A visible loss commits exactly once to the shared ledger; wins or unclear results pause without incrementing."));

        _lossWorkerHostA = new Panel { Width = 475, Height = 290, BackColor = PanelBg };
        _lossWorkerHostB = new Panel { Width = 475, Height = 290, BackColor = PanelBg };
        _lossWorkerHostA.Controls.Add(WorkerPlaceholder("Account A worker not initialized."));
        _lossWorkerHostB.Controls.Add(WorkerPlaceholder("Account B worker not initialized."));
        p.Controls.Add(_lossWorkerHostA);
        p.Controls.Add(_lossWorkerHostB);
    }

    private Control WorkerPlaceholder(string text)
        => new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = TextMuted,
            BackColor = FieldBg,
            TextAlign = ContentAlignment.MiddleCenter
        };

    private async Task<LossAutoCycleWorker> InitializeLossWorkerAsync(bool accountA, string? forcedProfileId = null)
    {
        var order = SelectedLossOrder();
        var profileId = forcedProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            profileId = order is not null
                ? (accountA ? order.ProfileAId : order.ProfileBId)
                : ProfileIdFromCombo(accountA ? _lossProfileACombo : _lossProfileBCombo);
        }

        var existing = accountA ? _lossWorkerA : _lossWorkerB;
        if (existing is not null && existing.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase) && existing.IsInitialized)
            return existing;

        if (existing is not null)
        {
            ReleaseClaimsForWorker(existing);
            existing.Dispose();
        }

        var profile = _settings.Profiles.FirstOrDefault(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null) throw new InvalidOperationException($"Commander profile '{profileId}' no longer exists.");

        var host = accountA ? _lossWorkerHostA : _lossWorkerHostB;
        host.Controls.Clear();
        host.Controls.Add(WorkerPlaceholder($"Initializing {profile.Name}…"));

        var env = await GetWebEnvironmentAsync(profile.Id);
        var worker = new LossAutoCycleWorker(accountA ? "A" : "B", profile.Id, profile.Name)
        {
            MessageHandler = HandleLossWorkerMessageAsync
        };
        host.Controls.Clear();
        worker.View.Dock = DockStyle.Fill;
        host.Controls.Add(worker.View);
        await worker.InitializeAsync(env);

        if (accountA) _lossWorkerA = worker; else _lossWorkerB = worker;
        FlashStatus($"Loss worker {(accountA ? "A" : "B")} initialized for {profile.Name}.");
        return worker;
    }

    private async Task StartLossAutoCycleAsync(bool accountA)
    {
        var order = SelectedLossOrder();
        if (order is null)
        {
            MessageBox.Show("Create or select a Loss Desk order first.", "Loss Auto Cycle");
            return;
        }
        if (order.Status == "Paused")
        {
            MessageBox.Show("The selected order is paused. Resume the order first.", "Loss Auto Cycle");
            return;
        }
        if (order.Remaining <= 0)
        {
            MessageBox.Show("The selected order has no losses remaining.", "Loss Auto Cycle");
            return;
        }

        if (order.ProfileAId.Equals(order.ProfileBId, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Auto Cycle requires Account A and Account B to be different Commander profiles so their Torn sessions and local worker state stay isolated. Edit/recreate the order with two distinct profiles.", "Loss Auto Cycle");
            return;
        }

        var profileId = accountA ? order.ProfileAId : order.ProfileBId;
        var key = CredentialManager.LoadApiKey(profileId);
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show($"Profile {(accountA ? "A" : "B")} does not have a saved Torn API key yet. Switch to that profile once and save/test its key in Settings.", "Loss Auto Cycle");
            return;
        }

        TornSnapshot profileSnapshot;
        try
        {
            profileSnapshot = await _api.FetchFastSnapshotAsync(key);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not identify the account from Torn API:\n\n" + ex.Message, "Loss Auto Cycle");
            return;
        }
        if (string.IsNullOrWhiteSpace(profileSnapshot.PlayerName) || profileSnapshot.PlayerId is null)
        {
            MessageBox.Show("The saved API key did not return the profile name/player ID. Auto-cycle did not start.", "Loss Auto Cycle");
            return;
        }
        if (profileSnapshot.PlayerId.Value.ToString() == order.TargetPlayerId)
        {
            MessageBox.Show("The loss-selling target cannot be the same account as the selected worker profile.", "Loss Auto Cycle");
            return;
        }

        var worker = await InitializeLossWorkerAsync(accountA, profileId);
        ReleaseClaimsForWorker(worker);
        order.Status = "Auto cycling";
        SaveLossOrders();
        RebindLossOrders();

        await worker.StartAsync(new
        {
            profileId,
            slot = accountA ? "A" : "B",
            orderId = order.Id,
            targetId = order.TargetPlayerId,
            selfName = profileSnapshot.PlayerName,
            maxKits = (int)_lossMaxKits.Value
        });
        FlashStatus($"Auto-cycle {(accountA ? "A" : "B")} armed for order {order.Id}. The worker will pause on ambiguous Torn UI instead of guessing.");
    }

    private async Task PauseLossWorkerAsync(bool accountA)
    {
        var worker = accountA ? _lossWorkerA : _lossWorkerB;
        if (worker is null) return;
        await worker.PauseAsync();
        FlashStatus($"Loss auto-cycle {(accountA ? "A" : "B")} paused.");
    }

    private async Task ResumeLossWorkerAsync(bool accountA)
    {
        var worker = accountA ? _lossWorkerA : _lossWorkerB;
        if (worker is null)
        {
            await InitializeLossWorkerAsync(accountA);
            worker = accountA ? _lossWorkerA : _lossWorkerB;
        }
        if (worker is null) return;
        await worker.ResumeAsync();
        FlashStatus($"Loss auto-cycle {(accountA ? "A" : "B")} resume requested.");
    }

    private async Task StopLossWorkerAsync(bool accountA)
    {
        var worker = accountA ? _lossWorkerA : _lossWorkerB;
        if (worker is null) return;
        await worker.StopAsync();
        ReleaseClaimsForWorker(worker);
        FlashStatus($"Loss auto-cycle {(accountA ? "A" : "B")} stopped.");
    }

    private async Task PauseBothLossWorkersAsync()
    {
        var tasks = new List<Task>();
        if (_lossWorkerA is not null) tasks.Add(_lossWorkerA.PauseAsync());
        if (_lossWorkerB is not null) tasks.Add(_lossWorkerB.PauseAsync());
        if (tasks.Count > 0) await Task.WhenAll(tasks);
        FlashStatus("Both loss auto-cycle workers paused.");
    }

    private async Task ResumeBothLossWorkersAsync()
    {
        var tasks = new List<Task>();
        if (_lossWorkerA is not null) tasks.Add(_lossWorkerA.ResumeAsync());
        if (_lossWorkerB is not null) tasks.Add(_lossWorkerB.ResumeAsync());
        if (tasks.Count > 0) await Task.WhenAll(tasks);
        FlashStatus("Resume requested for both initialized loss workers.");
    }

    private async Task StopBothLossWorkersAsync()
    {
        var tasks = new List<Task>();
        if (_lossWorkerA is not null) tasks.Add(_lossWorkerA.StopAsync());
        if (_lossWorkerB is not null) tasks.Add(_lossWorkerB.StopAsync());
        if (tasks.Count > 0) await Task.WhenAll(tasks);
        if (_lossWorkerA is not null) ReleaseClaimsForWorker(_lossWorkerA);
        if (_lossWorkerB is not null) ReleaseClaimsForWorker(_lossWorkerB);
        FlashStatus("Both loss auto-cycle workers stopped.");
    }

    private async Task HandleLossWorkerMessageAsync(LossAutoCycleWorker worker, JsonElement root)
    {
        var type = ReadWorkerString(root, "type");
        if (string.IsNullOrWhiteSpace(type)) return;

        if (type == "tc-loss-status")
        {
            var claimId = ReadWorkerString(root, "claimId");
            if (!string.IsNullOrWhiteSpace(claimId))
            {
                lock (_lossClaimGate)
                {
                    if (_lossClaims.TryGetValue(claimId, out var lease)) lease.LastSeen = DateTime.UtcNow;
                }
            }
            return;
        }

        if (type == "tc-loss-claim")
        {
            var requestId = ReadWorkerString(root, "requestId");
            var orderId = ReadWorkerString(root, "orderId");
            var result = RequestLossClaim(worker, orderId);
            await worker.ReplyClaimAsync(requestId, result.Ok, result.ClaimId, result.Message);
            return;
        }

        if (type == "tc-loss-commit")
        {
            var orderId = ReadWorkerString(root, "orderId");
            var claimId = ReadWorkerString(root, "claimId");
            var result = CommitLossClaim(worker, orderId, claimId);
            await worker.ReplyCommitAsync(result.Ok, result.Remaining, result.Message);
            return;
        }

        if (type == "tc-loss-abandon")
        {
            var claimId = ReadWorkerString(root, "claimId");
            AbandonLossClaim(worker, claimId);
            return;
        }

        if (type == "tc-loss-finished")
        {
            var orderId = ReadWorkerString(root, "orderId");
            var order = _lossOrders.FirstOrDefault(x => x.Id.Equals(orderId, StringComparison.OrdinalIgnoreCase));
            if (order is not null && order.Remaining <= 0)
            {
                order.Status = "Complete";
                SaveLossOrders();
                RebindLossOrders();
                FlashStatus($"Loss order {order.Id} completed after final recovery. Gross tracked: ${order.Gross:N0}.");
            }
        }
    }

    private (bool Ok, string ClaimId, string Message) RequestLossClaim(LossAutoCycleWorker worker, string orderId)
    {
        var order = _lossOrders.FirstOrDefault(x => x.Id.Equals(orderId, StringComparison.OrdinalIgnoreCase));
        if (order is null) return (false, "", "Order no longer exists.");
        if (order.Status == "Paused") return (false, "", "Order is paused.");
        if (order.Status == "Complete" || order.Remaining <= 0) return (false, "", "Order complete — no remaining losses.");

        var expectedProfile = worker.Slot == "A" ? order.ProfileAId : order.ProfileBId;
        if (!expectedProfile.Equals(worker.ProfileId, StringComparison.OrdinalIgnoreCase))
            return (false, "", $"Account {worker.Slot} is assigned to profile '{expectedProfile}', not '{worker.ProfileId}'.");

        lock (_lossClaimGate)
        {
            PruneLossClaimsLocked();

            var existingForWorker = _lossClaims.Values.FirstOrDefault(x => x.OrderId == order.Id && x.Slot == worker.Slot && x.ProfileId == worker.ProfileId);
            if (existingForWorker is not null)
            {
                existingForWorker.LastSeen = DateTime.UtcNow;
                return (true, existingForWorker.Id, "Existing loss slot restored.");
            }

            if (_lossClaims.Values.Any(x => x.TargetId == order.TargetPlayerId))
                return (false, "", "Target is busy on the other loss worker; waiting for the target lease.");

            var inFlightForOrder = _lossClaims.Values.Count(x => x.OrderId == order.Id);
            if (order.CompletedTotal + inFlightForOrder >= order.RequestedLosses)
                return (false, "", "No remaining unclaimed losses.");

            var lease = new LossClaimLease
            {
                OrderId = order.Id,
                TargetId = order.TargetPlayerId,
                Slot = worker.Slot,
                ProfileId = worker.ProfileId
            };
            _lossClaims[lease.Id] = lease;
            return (true, lease.Id, "Loss slot granted.");
        }
    }

    private (bool Ok, int Remaining, string Message) CommitLossClaim(LossAutoCycleWorker worker, string orderId, string claimId)
    {
        if (string.IsNullOrWhiteSpace(claimId)) return (false, 0, "Worker had no active claim to commit.");

        LossClaimLease? lease;
        lock (_lossClaimGate)
        {
            PruneLossClaimsLocked();
            if (!_lossClaims.TryGetValue(claimId, out lease)) return (false, 0, "Loss claim expired or was already released.");
            if (!lease.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase) || !lease.Slot.Equals(worker.Slot, StringComparison.OrdinalIgnoreCase) || !lease.ProfileId.Equals(worker.ProfileId, StringComparison.OrdinalIgnoreCase))
                return (false, 0, "Loss claim did not match this account/order.");
            _lossClaims.Remove(claimId);
        }

        var order = _lossOrders.FirstOrDefault(x => x.Id.Equals(orderId, StringComparison.OrdinalIgnoreCase));
        if (order is null) return (false, 0, "Order no longer exists.");
        if (order.Status == "Complete") return (false, order.Remaining, "Order was already marked complete; loss was not counted.");
        if (order.CompletedTotal >= order.RequestedLosses) return (false, order.Remaining, "Requested total was already reached; loss was not double-counted.");

        if (worker.Slot == "A")
        {
            order.CompletedA++;
            order.NextA = "Auto recovery";
        }
        else
        {
            order.CompletedB++;
            order.NextB = "Auto recovery";
        }

        order.Status = order.Remaining <= 0 ? "Final recovery" : "Auto cycling";
        SaveLossOrders();
        RebindLossOrders();
        FlashStatus($"Auto-cycle {worker.Slot} confirmed visible loss. {order.CompletedTotal}/{order.RequestedLosses} complete.");
        return (true, order.Remaining, $"Loss committed. {order.Remaining} remaining.");
    }

    private void AbandonLossClaim(LossAutoCycleWorker worker, string claimId)
    {
        if (string.IsNullOrWhiteSpace(claimId)) return;
        lock (_lossClaimGate)
        {
            if (_lossClaims.TryGetValue(claimId, out var lease) && lease.Slot == worker.Slot && lease.ProfileId == worker.ProfileId)
                _lossClaims.Remove(claimId);
        }
    }

    private void ReleaseClaimsForWorker(LossAutoCycleWorker worker)
    {
        lock (_lossClaimGate)
        {
            foreach (var id in _lossClaims.Values.Where(x => x.Slot == worker.Slot && x.ProfileId == worker.ProfileId).Select(x => x.Id).ToList())
                _lossClaims.Remove(id);
        }
    }

    private void PruneLossClaimsLocked()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-15);
        foreach (var id in _lossClaims.Values.Where(x => x.LastSeen < cutoff).Select(x => x.Id).ToList())
            _lossClaims.Remove(id);
    }

    private static string ReadWorkerString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";

    private void DisposeLossWorkers()
    {
        try { if (_lossWorkerA is not null) { ReleaseClaimsForWorker(_lossWorkerA); _lossWorkerA.Dispose(); } } catch { }
        try { if (_lossWorkerB is not null) { ReleaseClaimsForWorker(_lossWorkerB); _lossWorkerB.Dispose(); } } catch { }
        _lossWorkerA = null;
        _lossWorkerB = null;
    }
}
