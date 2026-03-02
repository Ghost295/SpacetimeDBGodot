using Godot;
using System;
using SpacetimeDB.Types;

namespace SpacetimeDB.Game.Lobby;

public partial class LobbyScreen : Control
{
    private Label _statusLabel;
    private VBoxContainer _matchRows;
    private Button _createMatchButton;
    private Button _refreshButton;

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>("Panel/Margin/Layout/StatusLabel");
        _matchRows = GetNode<VBoxContainer>("Panel/Margin/Layout/Scroll/MatchRows");
        _createMatchButton = GetNode<Button>("Panel/Margin/Layout/Buttons/CreateMatchButton");
        _refreshButton = GetNode<Button>("Panel/Margin/Layout/Buttons/RefreshButton");

        _createMatchButton.Pressed += OnCreateMatchPressed;
        _refreshButton.Pressed += BuildMatchList;

        var sync = GameCore.SpacetimeSync;
        sync.OnConnected += OnSyncStateChanged;
        sync.OnSubscriptionApplied += OnSyncStateChanged;
        sync.OnLobbyDataChanged += OnSyncStateChanged;

        BuildMatchList();
    }

    public override void _ExitTree()
    {
        if (_createMatchButton != null)
        {
            _createMatchButton.Pressed -= OnCreateMatchPressed;
        }

        if (_refreshButton != null)
        {
            _refreshButton.Pressed -= BuildMatchList;
        }

        var sync = GameCore.SpacetimeSync;
        if (sync != null)
        {
            sync.OnConnected -= OnSyncStateChanged;
            sync.OnSubscriptionApplied -= OnSyncStateChanged;
            sync.OnLobbyDataChanged -= OnSyncStateChanged;
        }
    }

    private void OnSyncStateChanged()
    {
        BuildMatchList();
    }

    private void OnCreateMatchPressed()
    {
        var sync = GameCore.SpacetimeSync;
        if (!sync.IsConnected())
        {
            _statusLabel.Text = "Not connected to server.";
            return;
        }

        if (sync.TryGetLocalMatchId(out _))
        {
            _statusLabel.Text = "Already joined a match.";
            return;
        }

        sync.CreateDebugMatch();
        _statusLabel.Text = "Creating match...";
    }

    private void OnJoinMatchPressed(ulong matchId)
    {
        var sync = GameCore.SpacetimeSync;
        if (!sync.IsConnected())
        {
            _statusLabel.Text = "Not connected to server.";
            return;
        }

        if (sync.TryGetLocalMatchId(out _))
        {
            _statusLabel.Text = "Already joined a match.";
            return;
        }

        sync.JoinMatch(matchId);
        _statusLabel.Text = $"Joining match {matchId}...";
    }

    private void OnRequestBattlePressed(ulong matchId)
    {
        var sync = GameCore.SpacetimeSync;
        if (!sync.IsConnected())
        {
            _statusLabel.Text = "Not connected to server.";
            return;
        }

        if (!sync.TryGetLocalMatch(out var localMatch) || localMatch.MatchId != matchId)
        {
            _statusLabel.Text = "Join the match first.";
            return;
        }

        sync.RequestBattleStart();
        _statusLabel.Text = $"Ready sent for match {matchId}.";
    }

    private void BuildMatchList()
    {
        var sync = GameCore.SpacetimeSync;
        if (!sync.IsConnected())
        {
            SetLobbyVisible(true);
            ClearMatchRows();
            _statusLabel.Text = "Connecting...";
            return;
        }

        if (sync.HasActiveBattle)
        {
            SetLobbyVisible(false);
            return;
        }

        SetLobbyVisible(true);
        ClearMatchRows();

        var matches = sync.GetOpenMatches();
        var hasLocalMatch = sync.TryGetLocalMatchId(out var localMatchId);
        var hasLocalShopState = sync.TryGetLocalShopState(out var localShopState);
        if (hasLocalMatch && sync.TryGetLocalMatch(out var localMatch))
        {
            var phase = MatchPhaseLabel(localMatch.Status);
            if (hasLocalShopState)
            {
                _statusLabel.Text =
                    $"Match {localMatchId} ({phase}) | Shop R{localShopState.RoundNumber} | Gold {localShopState.Gold} | Lvl {localShopState.ShopLevel} | Offers {sync.GetLocalShopOffers().Count}";
            }
            else
            {
                _statusLabel.Text = $"Joined match {localMatchId} ({phase}).";
            }
        }
        else
        {
            _statusLabel.Text = "Not joined to a match.";
        }

        if (matches.Count == 0)
        {
            _matchRows.AddChild(new Label { Text = "No open matches." });
            return;
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var row = new HBoxContainer();
            row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            var label = new Label();
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.Text =
                $"Match {match.MatchId} | Players: {match.ActivePlayers} | Host: {ShortIdentity(match.CreatedBy)}";
            row.AddChild(label);

            var joinButton = new Button();
            joinButton.Text = hasLocalMatch && localMatchId == match.MatchId ? "Joined" : "Join";
            joinButton.Disabled = hasLocalMatch || match.ActivePlayers >= 2;
            var capturedMatchId = match.MatchId;
            joinButton.Pressed += () => OnJoinMatchPressed(capturedMatchId);
            row.AddChild(joinButton);

            var isLocalMatch = hasLocalMatch && localMatchId == match.MatchId;
            var isShopPhase = match.Status == 1;
            var hasReadyRequested = isLocalMatch && hasLocalShopState && localShopState.RequestedBattleStart;
            var readyButton = new Button();
            readyButton.Text = hasReadyRequested ? "Ready Sent" : "Ready";
            readyButton.Disabled = !isLocalMatch || !isShopPhase || !hasLocalShopState || hasReadyRequested;
            readyButton.Pressed += () => OnRequestBattlePressed(capturedMatchId);
            row.AddChild(readyButton);

            _matchRows.AddChild(row);
        }
    }

    private void ClearMatchRows()
    {
        var children = _matchRows.GetChildren();
        for (var i = 0; i < children.Count; i++)
        {
            children[i].QueueFree();
        }
    }

    private void SetLobbyVisible(bool isVisible)
    {
        Visible = isVisible;
        Input.MouseMode = isVisible
            ? Input.MouseModeEnum.Visible
            : Input.MouseModeEnum.Captured;
    }

    private static string ShortIdentity(Identity identity)
    {
        var value = identity.ToString();
        if (string.IsNullOrEmpty(value) || value.Length <= 12)
        {
            return value;
        }

        return $"{value[..6]}...{value[^4..]}";
    }

    private static string MatchPhaseLabel(byte status)
    {
        return status switch
        {
            1 => "Shop",
            2 => "Battle",
            3 => "Complete",
            _ => "Lobby"
        };
    }
}
