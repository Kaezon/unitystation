using System;
using System.Collections.Generic;
using PlayGroup;
using Tilemaps;
using Tilemaps.Behaviours.Objects;
using UnityEngine;
using UnityEngine.Networking;
using Random = UnityEngine.Random;

// ReSharper disable CompareOfFloatsByEqualityOperator
public struct TransformState {
	public bool Active => Position != HiddenPos;
	public float speed; //public in order to serialize :\
	public float Speed {
		get { return speed; }
		set {
			if ( value < 0 ) {
				speed = 0;
			} else {
				speed = value;
			}
		}
	}
	
	///Direction of throw
	public Vector2 Impulse;

	/// Server-only active throw information
	[NonSerialized]
	public ThrowInfo ActiveThrow;

	public int MatrixId;
	public Vector3 Position;

	public float Rotation;
	public sbyte SpinFactor; 
	
	/// Means that this object is hidden
	public static readonly Vector3Int HiddenPos = new Vector3Int(0, 0, -100);
	/// Means that this object is hidden
	public static readonly TransformState HiddenState = 
		new TransformState{ Position = HiddenPos, ActiveThrow = ThrowInfo.NoThrow, MatrixId = 0};

	public Vector3 WorldPosition
	{
		get
		{
			if ( !Active )
			{
				return HiddenPos;
			}
			MatrixInfo matrix = MatrixManager.Get( MatrixId );
			return MatrixManager.LocalToWorld( Position, matrix );
		}
		set
		{
			if (value == HiddenPos) {
				this = HiddenState;
			}
			else
			{
				MatrixInfo matrix = MatrixManager.Get( MatrixId );
				Position = MatrixManager.WorldToLocal( value, matrix );
			}
		}
	}

	public override string ToString()
	{
		return Equals( HiddenState ) ? "[Hidden]" : $"[{nameof( Position )}: {(Vector2)Position}, {nameof( WorldPosition )}: {(Vector2)WorldPosition}, " +
		       $"{nameof( Speed )}: {Speed}, {nameof( Impulse )}: {Impulse}, {nameof( Rotation )}: {Rotation}, {nameof( SpinFactor )}: {SpinFactor}, " +
		                                            $" {nameof( MatrixId )}: {MatrixId}]";
	}
}

public partial class CustomNetTransform : ManagedNetworkBehaviour //see UpdateManager
{
	private RegisterTile registerTile;
	private ItemAttributes itemAttributes;

	private TransformState serverState = TransformState.HiddenState; //used for syncing with players, matters only for server

	private TransformState clientState = TransformState.HiddenState; //client's transform, can get dirty/predictive

	private Matrix matrix => registerTile.Matrix;
	
	public TransformState ServerState => serverState;
	public TransformState ClientState => clientState;

//	[SyncVar]

	private void Start()
	{
		clientState = TransformState.HiddenState;
		registerTile = GetComponent<RegisterTile>();
		itemAttributes = GetComponent<ItemAttributes>();
	}

	public override void OnStartServer()
	{
		base.OnStartServer();
		InitServerState();
	}

	[Server]
	private void InitServerState()
	{ //todo: fix runtime spawn matrixid init nagging
//		isPushing = false;
//		predictivePushing = false;

		serverState.Speed = 0;
		//If object is supposed to be hidden, keep it that way
		if ( IsHiddenOnInit ) {
			return;
		}

		serverState.Position =
			Vector3Int.RoundToInt(new Vector3(transform.localPosition.x, transform.localPosition.y, 0));
		serverState.Rotation = 0f;
		serverState.SpinFactor = 0;
		registerTile = GetComponent<RegisterTile>();
		if ( registerTile && registerTile.Matrix && MatrixManager.Instance ) {
			serverState.MatrixId = MatrixManager.Get( matrix ).Id;
		} else {
			Debug.LogWarning( "Matrix id init failure" );
		}
	}

	/// Is it supposed to be hidden? (For init purposes)
	private bool IsHiddenOnInit {
		get {
			if ( Vector3Int.RoundToInt( transform.position ).Equals( TransformState.HiddenPos ) ||
			     Vector3Int.RoundToInt( transform.localPosition ).Equals( TransformState.HiddenPos ) ) {
				return true;
			}
			VisibleBehaviour visibleBehaviour = GetComponent<VisibleBehaviour>();
			return visibleBehaviour && !visibleBehaviour.visibleState;
		}
	}

	/// Intended for runtime spawning, so that CNT could initialize accordingly
	[Server]
	public void ReInitServerState()
	{
		InitServerState();
	//	Debug.Log($"{name} reInit: {serverTransformState}");
	}

	private void Synchronize()
	{
		if (!clientState.Active)
		{
			return;
		}

		if (isServer)
		{
			CheckFloating();
//			//Sync the pushing state to all players
//			//this makes sure that players with high pings cannot get too
//			//far with prediction
//			if(isPushing){
//				if(clientState.Position == transform.localPosition){
//					isPushing = false;
//					predictivePushing = false;
//				}
//			}
		} 

		if (IsFloatingClient)
		{
			SimulateFloating();
			//fixme: don't simulate moving through solid stuff on client
		}

		if (clientState.Position != transform.localPosition)
		{
			Lerp();
		}

		if ( clientState.SpinFactor != 0 ) {
			transform.Rotate( Vector3.forward, Time.deltaTime * clientState.Speed * clientState.SpinFactor );
		}

		//Checking if we should change matrix once per tile
		if (isServer && registerTile.Position != Vector3Int.RoundToInt(serverState.Position) ) {
			//Todo: optimize usage, checking every tile is kind of expensive, and all things flying into abyss are going to do that
			CheckMatrixSwitch();
		}
		//Registering
		if (registerTile.Position != Vector3Int.RoundToInt(clientState.Position) )//&& !isPushing && !predictivePushing)
		{
//			Debug.LogFormat($"registerTile updating {registerTile.WorldPosition}->{Vector3Int.RoundToInt(clientState.WorldPosition)} ");
			RegisterObjects();
		}
	}

	/// Manually set an item to a specific position. Use WorldPosition!
	[Server]
	public void SetPosition(Vector3 worldPos, bool notify = true/*, float speed = 4f, bool _isPushing = false*/) {
//		Only allow one movement at a time if it is currently being pushed
//		if(isPushing || predictivePushing){
//			if(predictivePushing && _isPushing){
//				This is if the server player is pushing because the predictive flag
				//will be set early we still need to notify the players so call it here:
//				UpdateServerTransformState(pos, notify, speed);
				//And then set the isPushing flag:
//				isPushing = true;
//			}
//			return;
//		}
		var pos = (Vector2) worldPos; //Cut z-axis
		serverState.MatrixId = MatrixManager.AtPoint( Vector3Int.RoundToInt( worldPos ) ).Id;
//		serverState.Speed = speed;
		serverState.WorldPosition = pos;
		if (notify) {
			NotifyPlayers();
		}
		//Set it to being pushed if it is a push net action
//		if(_isPushing){
			//This is synced via syncvar with all players
//			isPushing = true;
//		}
	}

	[Server]
	private void SyncMatrix() {
		registerTile.ParentNetId = MatrixManager.Get( serverState.MatrixId ).NetId;
	}

	[Server]
	private void CheckMatrixSwitch( bool notify = true ) {
		int newMatrixId = MatrixManager.AtPoint( Vector3Int.RoundToInt( serverState.WorldPosition ) ).Id;
		if ( serverState.MatrixId != newMatrixId ) {
			Debug.Log( $"{gameObject} matrix {serverState.MatrixId}->{newMatrixId}" );

			//It's very important to save World Pos before matrix switch and restore it back afterwards
			var worldPosToPreserve = serverState.WorldPosition;
			serverState.MatrixId = newMatrixId;
			serverState.WorldPosition = worldPosToPreserve;
			if ( notify ) {
				NotifyPlayers();
			}
		}
	}

	[Server]
	public void DisappearFromWorldServer()
	{
		serverState = TransformState.HiddenState;
		NotifyPlayers();
	}

	[Server]
	public void AppearAtPositionServer(Vector3 worldPos)
	{
		SetPosition(worldPos);
	}

//	///     Method to substitute transform.parent = x stuff.
//	///     You shouldn't really use it anymore,
//	///     as there are high level methods that should suit your needs better.
//	///     Server-only, client is not being notified
//	[Server]
//	public void SetParent(Transform pos)
//	{
//		transform.parent = pos;
//	}

	///     Convenience method to make stuff disappear at position.
	///     For CLIENT prediction purposes.
	public void DisappearFromWorld()
	{
		clientState = TransformState.HiddenState;
		updateActiveStatus();
	}

	///     Convenience method to make stuff appear at position
	///     For CLIENT prediction purposes.
	public void AppearAtPosition(Vector3 worldPos)
	{
		var pos = (Vector2) worldPos; //Cut z-axis
		clientState.MatrixId = MatrixManager.AtPoint( Vector3Int.RoundToInt( worldPos ) ).Id;
		clientState.WorldPosition = pos;
		transform.position = pos;
		updateActiveStatus();
	}

	public void UpdateClientState(TransformState newState)
	{
		//FIXME: invesigate wrong clientState for microwaved food
		//todo: address rotation pickup issues + items should be upright when dropping
		//Don't lerp (instantly change pos) if active state was changed
		if (clientState.Active != newState.Active /*|| newState.Speed == 0*/)
		{
			transform.localPosition = newState.Position;
		}
		clientState = newState;
		updateActiveStatus();
//		transform.rotation = Quaternion.Euler( 0, 0, clientState.Rotation ); //?
	}

	private void updateActiveStatus()
	{
		if (clientState.Active)
		{
			RegisterObjects();
		}
		else
		{
			registerTile.Unregister();
		}
		//Consider moving VisibleBehaviour functionality to CNT. Currently VB doesn't allow predictive object hiding, for example. 
		Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			renderers[i].enabled = clientState.Active;
		}
	}

	///     Currently sending to everybody, but should be sent to nearby players only
	[Server]
	private void NotifyPlayers()
	{
		Debug.Log( $"{gameObject.name} Notified" );
		SyncMatrix();
		TransformStateMessage.SendToAll(gameObject, serverState);
	}

	///     Sync with new player joining
	/// <param name="playerGameObject"></param>
	[Server]
	public void NotifyPlayer(GameObject playerGameObject)
	{
		TransformStateMessage.Send(playerGameObject, gameObject, serverState);
	}

	//managed by UpdateManager
	public override void UpdateMe()
	{
		if (!registerTile)
		{
			registerTile = GetComponent<RegisterTile>();
		}
		Synchronize();
	}

	private void RegisterObjects() {
		//Register item pos in matrix
		registerTile.UpdatePosition();
	}

//	//For space drift, the server will confirm an update is required and inform the clients
//	[ClientRpc]
//	private void RpcForceRegisterUpdate(){
//		registerTile.UpdatePosition();
//	}
}