using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using SpacetimeDB;
using SpacetimeDB.Game.VAT;
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

    public Dictionary<int, VATInstanceHandle> Entities = new();
    
    public VATModel Model { get; set; }

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
        
        conn.Db.Boid.OnInsert += BoidOnInsert;
        conn.Db.Boid.OnUpdate += BoidOnUpdate;
        conn.Db.Boid.OnDelete += BoidOnDelete;
        
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

    private void BoidOnInsert(EventContext context, Boid insertedValue)
    {
        // GD.Print($"Entity inserted: {insertedValue.BoidId}");
        
        var handle = GameCore.VATModelManager.SpawnInstance(Model, new Transform3D(Basis.Identity, new Vector3(insertedValue.Position.X, 2, insertedValue.Position.Y)));
        
        Entities[insertedValue.BoidId] = handle;
    }

    private void BoidOnUpdate(EventContext context, Boid oldEntity, Boid newEntity)
    {
        // GD.Print($"Entity updated: {newEntity.BoidId}");
        if (!Entities.TryGetValue(newEntity.BoidId, out var entityController))
        {
            return;
        }

        var handle = Entities[newEntity.BoidId];
        
        GameCore.VATModelManager.SetInstanceTransform(handle, new Transform3D(Basis.Identity, new Vector3(newEntity.Position.X, 1, newEntity.Position.Y)));
    }

    private void BoidOnDelete(EventContext context, Boid oldEntity)
    {
        // GD.Print($"Entity deleted: {oldEntity.BoidId}");
        if (Entities.Remove(oldEntity.BoidId, out var handle))
        {
            GameCore.VATModelManager.DestroyInstance(handle);
        }
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
