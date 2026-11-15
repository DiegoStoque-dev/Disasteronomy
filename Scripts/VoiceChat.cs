#nullable enable
using Godot;
using System;
using System.Collections.Generic;

public partial class VoiceChat : Node
{
	private double debugTimer = 0;
	private AudioStreamPlayer? microphonePlayer;
	private AudioEffectCapture? capture;
	private Button? voiceIndicator;

	private const int FramesPerPacket = 480;

	private bool microphoneEnabled = false;

	private Dictionary<long, AudioStreamPlayer> remotePlayers = new();
	private Dictionary<long, AudioStreamGeneratorPlayback> remotePlaybacks = new();

	public override void _Ready()
	{
		GD.Print("VOICE CHAT STARTED!");

		// The indicator lives in the lobby panel, not under this node, so look it up
		// from the scene root as well (FindChild only searches this node's own subtree).
		voiceIndicator = GetNodeOrNull<Button>("../CanvasLayer/Center/Panel/VBoxContainer/VoiceIndicator")
			?? GetNodeOrNull<Button>("../CanvasLayer/VBoxContainer/VoiceIndicator")
			?? GetParent()?.FindChild("VoiceIndicator", recursive: true, owned: false) as Button;
		microphonePlayer = GetNodeOrNull<AudioStreamPlayer>("../Microphone");

		if (voiceIndicator == null)
		{
			GD.PrintErr("VoiceIndicator not found.");
			SetProcess(false);
			return;
		}

		if (microphonePlayer == null)
		{
			GD.PrintErr("Microphone node not found.");
			SetProcess(false);
			return;
		}

		if (AudioServer.GetBusIndex("Microphone") < 0)
		{
			GD.PrintErr("Microphone audio bus not found.");
			SetProcess(false);
			return;
		}

		microphonePlayer.Bus = "Microphone";

		voiceIndicator.MouseFilter = Control.MouseFilterEnum.Ignore;
		voiceIndicator.FocusMode = Control.FocusModeEnum.None;
		voiceIndicator.Text = "Microphone off";
		voiceIndicator.Modulate = Colors.Gray;

		// Look up the microphone audio bus.
		int busIndex = AudioServer.GetBusIndex("Microphone");

		if (busIndex == -1)
		{
			GD.PrintErr("Microphone audio bus not found.");
			SetProcess(false);
			return;
		}

		// Look for the AudioEffectCapture on that bus.
		for (int i = 0; i < AudioServer.GetBusEffectCount(busIndex); i++)
		{
			AudioEffect effect = AudioServer.GetBusEffect(busIndex, i);

			if (effect is AudioEffectCapture audioCapture)
			{
				capture = audioCapture;
				break;
			}
		}

		if (capture == null)
		{
			GD.PrintErr("AudioEffectCapture not found.");
			SetProcess(false);
			return;
		}

		GD.Print("Voice system ready!");
	}

	public override void _Process(double delta)
	{
		bool pressed = Input.IsActionPressed("voice_chat");

		// Turn the microphone on.
		if (pressed && !microphoneEnabled)
		{
			microphoneEnabled = true;
			if (microphonePlayer != null)
				microphonePlayer.Play();

			if (voiceIndicator != null)
			{
				voiceIndicator.Text = "Talking...";
				voiceIndicator.Modulate = Colors.LimeGreen;
			}

			GD.Print("Microphone enabled!");
		}

		// Turn the microphone off.
		else if (!pressed && microphoneEnabled)
		{
			microphoneEnabled = false;
			if (microphonePlayer != null)
				microphonePlayer.Stop();

			if (voiceIndicator != null)
			{
				voiceIndicator.Text = "Microphone off";
				voiceIndicator.Modulate = Colors.Gray;
			}

			GD.Print("Microphone disabled!");
		}

		if (!microphoneEnabled || capture == null)
			return;

		if (Multiplayer.MultiplayerPeer == null)
			return;

		// Grab the captured audio frames.
		int available = capture.GetFramesAvailable();
		debugTimer += delta;

if (debugTimer >= 1.0)
{
	GD.Print("Frames available: ", available);
	debugTimer = 0;
}
		if (available >= FramesPerPacket)
		{
			Vector2[] samples = capture.GetBuffer(FramesPerPacket);

			byte[] packet = EncodeAudio(samples);

			// Host sends its own voice.
			if (Multiplayer.IsServer())
			{
				BroadcastVoice(Multiplayer.GetUniqueId(), packet);
			}
			// Client sends to the host.
			else if (
				Multiplayer.MultiplayerPeer
					.GetConnectionStatus()
				== MultiplayerPeer.ConnectionStatus.Connected
			)
			{
				RpcId(1, nameof(ReceiveVoice), packet);
			}
		}
	}

	// Converts audio to 16-bit mono.
	private byte[] EncodeAudio(Vector2[] samples)
	{
		byte[] packet = new byte[samples.Length * 2];

		for (int i = 0; i < samples.Length; i++)
		{
			float mono = (samples[i].X + samples[i].Y) / 2f;

			mono = Mathf.Clamp(mono, -1f, 1f);

			short value = (short)(mono * 32767);

			packet[i * 2] = (byte)(value & 0xFF);
			packet[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
		}

		return packet;
	}

	// Receives the audio sent by clients.
	[Rpc(
		MultiplayerApi.RpcMode.AnyPeer,
		CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable,
		TransferChannel = 1
	)]
	public void ReceiveVoice(byte[] packet)
	{
		if (!Multiplayer.IsServer())
			return;

		if (packet == null || packet.Length == 0 ||
			packet.Length > 1200 || packet.Length % 2 != 0)
			return;

		long senderId = Multiplayer.GetRemoteSenderId();

		BroadcastVoice(senderId, packet);
	}

	// Encaminha a voz aos outros jogadores
	private void BroadcastVoice(long senderId, byte[] packet)
	{
		// O host reproduz a voz recebida dos clientes
		if (senderId != Multiplayer.GetUniqueId())
		{
			PlayRemoteVoice(senderId, packet);
		}

		foreach (int peerId in Multiplayer.GetPeers())
		{
			if (peerId == senderId)
				continue;

			RpcId(
				peerId,
				nameof(PlayRemoteVoice),
				senderId,
				packet
			);
		}
	}

	// Recebe e reproduz a voz de outro jogador
	[Rpc(
		MultiplayerApi.RpcMode.Authority,
		CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable,
		TransferChannel = 1
	)]
	public void PlayRemoteVoice(long senderId, byte[] packet)
	{
		if (packet == null || packet.Length == 0 ||
			packet.Length % 2 != 0 || packet.Length > 1200)
			return;

		if (!remotePlaybacks.TryGetValue(
			senderId,
			out AudioStreamGeneratorPlayback? playback))
		{
			playback = CreateRemotePlayer(senderId);
		}

		if (playback == null)
			return;

		int frameCount = packet.Length / 2;

		Vector2[] frames = new Vector2[frameCount];

		for (int i = 0; i < frameCount; i++)
		{
			short sample = unchecked((short)(
				packet[i * 2] |
				(packet[i * 2 + 1] << 8)
			));

			float value = sample / 32768f;

			frames[i] = new Vector2(value, value);
		}

		if (playback.CanPushBuffer(frameCount))
		{
			playback.PushBuffer(frames);
		}
	}

	// Cria um reprodutor para cada jogador
	private AudioStreamGeneratorPlayback? CreateRemotePlayer(long peerId)
	{
		AudioStreamGenerator generator = new AudioStreamGenerator();

		generator.MixRate = AudioServer.GetMixRate();
		generator.BufferLength = 0.2f;

		AudioStreamPlayer player = new AudioStreamPlayer();

		player.Name = $"Voice_{peerId}";
		player.Stream = generator;

		AddChild(player);

		player.Play();

		AudioStreamGeneratorPlayback? playback =
			player.GetStreamPlayback() as AudioStreamGeneratorPlayback;

		if (playback == null)
		{
			player.QueueFree();
			return null;
		}

		remotePlayers[peerId] = player;
		remotePlaybacks[peerId] = playback;

		return playback;
	}
}
