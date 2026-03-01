using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace SpacetimeDB.Game;

public class SpacetimeSync
{
    const string SERVER_URL = "http://127.0.0.1:3000";
    const string MODULE_NAME = "big-battles";

    public event Action OnConnected;
    public event Action OnSubscriptionApplied;
    
    public Identity LocalIdentity { get; private set; }
    public DbConnection Conn { get; private set; }

    public List<Entity> Entities = new();

    private HashSet<int> _pendingConsumeAnimations = new();

    public void Start()
    {
        // Clear game state in case we've disconnected and reconnected
        Entities.Clear();
        _pendingConsumeAnimations.Clear();

        // In order to build a connection to SpacetimeDB we need to register
        // our callbacks and specify a SpacetimeDB server URI and module name.
        var builder = DbConnection.Builder()
            .OnConnect(HandleConnect)
            .OnConnectError(HandleConnectError)
            .OnDisconnect(HandleDisconnect)
            .WithUri(SERVER_URL)
            .WithDatabaseName(MODULE_NAME);

        // If the user has a SpacetimeDB auth token stored in the Unity PlayerPrefs,
        // we can use it to authenticate the connection.
        // For testing purposes, it is often convenient to comment the following lines out and
        // export an executable for the project using File -> Build Settings.
        // Then, you can run the executable multiple times. Since the executable will not check for
        // a saved auth token, each run of will receive a different Identifier,
        // and their circles will be able to eat each other.
        // if (AuthToken.Token != "")
        // {
        //     GD.Print("Using auth token!");
        //     builder = builder.WithToken(AuthToken.Token);
        // }

        // Building the connection will establish a connection to the SpacetimeDB
        // server.
        Conn = builder.Build();
    }

    // Called when we connect to SpacetimeDB and receive our client identity
    void HandleConnect(DbConnection conn, Identity identity, string token)
    {
        GD.Print("Connected!");
        // AuthToken.SaveToken(token);
        
        GD.Print($"Token: {token}");
        
        LocalIdentity = identity;
        GD.Print($"Local identity: {identity}");
        
        Conn.Reducers.SayHello();
        
        GD.Print("Said hello");
        
        conn.Db.Entity.OnInsert += EntityOnInsert;
        conn.Db.Entity.OnUpdate += EntityOnUpdate;
        conn.Db.Entity.OnDelete += EntityOnDelete;
        
        GD.Print("Registered event handlers");

        OnConnected?.Invoke();
        
        // Request all tables
        Conn.SubscriptionBuilder()
            .OnApplied(HandleSubscriptionApplied)
            .SubscribeToAllTables();
        
        GD.Print("Subscribed to all tables");
    }

    public void Update()
    {
        Conn.FrameTick();
    }

    void HandleConnectError(Exception ex)
    {
        GD.Print($"Connection error: {ex}");
    }

    void HandleDisconnect(DbConnection _conn, Exception ex)
    {
        GD.Print("Disconnected.");
        if (ex != null)
        {
            GD.PrintErr(ex);
        }
    }

    private void HandleSubscriptionApplied(SubscriptionEventContext ctx)
    {
        GD.Print("Subscription applied!");
        OnSubscriptionApplied?.Invoke();

        // Once we have the initial subscription sync'd to the client cache
        // Get the world size from the config table and set up the arena
        // var worldSize = Conn.Db.Config.Id.Find(0).WorldSize;
    }

    private void EntityOnInsert(EventContext context, Entity insertedValue)
    {
        GD.Print($"Entity inserted: {insertedValue.EntityId}");
        Entities.Add(insertedValue);
    }

    private void EntityOnUpdate(EventContext context, Entity oldEntity, Entity newEntity)
    {
        GD.Print($"Entity updated: {newEntity.EntityId}");
        // if (!Entities.TryGetValue(newEntity.EntityId, out var entityController))
        // {
        //     return;
        // }
        // entityController.OnEntityUpdated(newEntity);
    }

    private void EntityOnDelete(EventContext context, Entity oldEntity)
    {
        GD.Print($"Entity deleted: {oldEntity.EntityId}");
        Entities.Remove(oldEntity);
        // if (Entities.Remove(oldEntity.EntityId, out var entityController))
        // {
        //     if (_pendingConsumeAnimations.Remove(oldEntity.EntityId))
        //     {
        //         // Already being animated by ConsumeEntityEventOnInsert — don't destroy yet
        //         return;
        //     }
        //     entityController.OnDelete(context);
        // }
    }
    

    // private static PlayerController GetOrCreatePlayer(int playerId)
    // {
    //     if (!Players.TryGetValue(playerId, out var playerController))
    //     {
    //         var player = Conn.Db.Player.PlayerId.Find(playerId);
    //         playerController = PrefabManager.SpawnPlayer(player);
    //         Players.Add(playerId, playerController);
    //     }
    //
    //     return playerController;
    // }

    public bool IsConnected()
    {
        return Conn != null && Conn.IsActive;
    }

    public void Disconnect()
    {
        Conn.Disconnect();
        Conn = null;
    }
}
