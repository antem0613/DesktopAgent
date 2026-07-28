using System;
using System.Collections.Generic;
using Unity.Logging;
using UnityEngine;

/// <summary>
/// メインプロセスで動作するキャラクターの集約管理コンポーネント。
/// キャラクタープロセスのライフサイクル・IPC・LLM/TTS/STT連携を担う。
/// </summary>
public class CompanionController : SingletonMonoBehaviour<CompanionController>
{
    private const string LogPrefix = "[CompanionController]";

    // --- Character State ---

    /// <summary>いずれかのキャラクターがスポーン済みのとき true。</summary>
    public bool IsAnyCharacterSpawned
    {
        get
        {
            foreach (var entry in _characters.Values)
            {
                if (entry.IsSpawned) return true;
            }
            return false;
        }
    }

    /// <summary>キャラクタースポーン完了時に発火する。引数は characterId。</summary>
    public event Action<int> OnCharacterSpawnCompleted;

    private readonly Dictionary<int, CharacterEntry> _characters = new();

    // --- Lifecycle ---

    private protected override void Awake()
    {
        base.Awake();
        Log.Info($"{LogPrefix} Awake.");
    }

    private void Start()
    {
        Log.Info($"{LogPrefix} Start.");
        TryStartDefaultCharacterProcess();
    }

    private void TryStartDefaultCharacterProcess()
    {
        var processIdentifier = ProcessIdentifier.Instance;
        if (processIdentifier == null)
        {
            Log.Warning($"{LogPrefix} TryStartDefaultCharacterProcess: ProcessIdentifier is not available.");
            return;
        }

        int optionVal = processIdentifier.DefaultCharacterOptionVal;
        if (optionVal < 0)
        {
            Log.Info($"{LogPrefix} TryStartDefaultCharacterProcess: no default character scheduled (optionVal={optionVal}).");
            return;
        }

        Log.Info($"{LogPrefix} TryStartDefaultCharacterProcess: starting default character process. optionVal={optionVal}");
        bool success = processIdentifier.TryStartCharacterProcess(optionVal);
        Log.Info($"{LogPrefix} TryStartDefaultCharacterProcess: result={success}, optionVal={optionVal}");
    }

    private void OnDestroy()
    {
        Log.Info($"{LogPrefix} OnDestroy.");
        _characters.Clear();
    }

    // --- Character Spawn ---

    /// <summary>
    /// 指定プレハブパスのキャラクタープロセスを起動する。
    /// optionVal にはプレハブの <see cref="CompanionDescripter.CharacterId"/> を使用する。
    /// </summary>
    public bool SpawnCharacter(string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
        {
            Log.Warning($"{LogPrefix} SpawnCharacter: prefabPath is empty.");
            return false;
        }

        var processIdentifier = ProcessIdentifier.Instance;
        if (processIdentifier == null)
        {
            Log.Warning($"{LogPrefix} SpawnCharacter: ProcessIdentifier is not available.");
            return false;
        }

        // プレハブから CompanionDescripter を取得し、CharacterId を optionVal として使用する
        var prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            Log.Warning($"{LogPrefix} SpawnCharacter: prefab not found at '{prefabPath}'.");
            return false;
        }

        var descriptr = prefab.GetComponent<CompanionDescripter>();
        if (descriptr == null)
        {
            Log.Warning($"{LogPrefix} SpawnCharacter: CompanionDescripter not found on prefab '{prefabPath}'.");
            return false;
        }

        int optionVal = descriptr.CharacterId;
        Log.Info($"{LogPrefix} SpawnCharacter: starting process. prefabPath={prefabPath}, optionVal={optionVal}");
        bool success = processIdentifier.TryStartCharacterProcess(optionVal);
        Log.Info($"{LogPrefix} SpawnCharacter: result={success}, optionVal={optionVal}");
        return success;
    }

    // --- Speech Instruction ---

    /// <summary>
    /// LLM 応答をキャラクターへの発話として指示する。
    /// メインプロセスで TTS を再生し、キャラクタープロセスへの発話コマンドを送信する。
    /// </summary>
    public void RequestSpeech(string text, int characterId = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Log.Warning($"{LogPrefix} RequestSpeech: text is empty.");
            return;
        }

        Log.Info($"{LogPrefix} RequestSpeech: characterId={characterId}, textLength={text.Length}");

        var ttsManager = TTSManager.Instance;
        if (ttsManager == null)
        {
            Log.Warning($"{LogPrefix} RequestSpeech: TTSManager is not available.");
        } else
        {
            ttsManager.PlayTts(text);
        }

        SendSpeechCommandToCharacter(characterId, text);
    }

    private void SendSpeechCommandToCharacter(int characterId, string text)
    {
        // TODO: IPC (UDP 等) でキャラクタープロセスへ発話コマンドを送信する。
        Log.Info($"{LogPrefix} SendSpeechCommandToCharacter: stub. characterId={characterId}, textLength={text.Length}");
    }

    // --- IPC Receive (Phase 3 で BackendManager から呼び出す) ---

    /// <summary>
    /// キャラクタープロセスから受信した状態メッセージを処理する。
    /// BackendManager の WebSocket 受信コールバックから呼び出す想定。
    /// </summary>
    public void HandleCharacterStateMessage(CharacterStateMessage message)
    {
        if (message == null)
        {
            Log.Warning($"{LogPrefix} HandleCharacterStateMessage: message is null.");
            return;
        }

        Log.Info($"{LogPrefix} HandleCharacterStateMessage: messageType={message.messageType}, characterId={message.characterId}");

        switch (message.messageType)
        {
            case "CharacterSpawnCompleted":
                HandleCharacterSpawnCompleted(message);
                break;
            default:
                Log.Info($"{LogPrefix} HandleCharacterStateMessage: unhandled messageType={message.messageType}.");
                break;
        }
    }

    // --- Message Handlers ---

    private void HandleCharacterSpawnCompleted(CharacterStateMessage message)
    {
        if (!int.TryParse(message.characterId, out int characterId))
        {
            Log.Warning($"{LogPrefix} HandleCharacterSpawnCompleted: invalid characterId='{message.characterId}'.");
            return;
        }

        if (!_characters.TryGetValue(characterId, out var entry))
        {
            entry = new CharacterEntry { CharacterId = characterId };
            _characters[characterId] = entry;
            Log.Info($"{LogPrefix} HandleCharacterSpawnCompleted: new character registered. characterId={characterId}");
        }

        entry.IsSpawned = true;
        entry.IsConnected = true;

        Log.Info($"{LogPrefix} HandleCharacterSpawnCompleted: completed. characterId={characterId}, status={message.status}");
        OnCharacterSpawnCompleted?.Invoke(characterId);
    }

    // --- Inner Types ---

    private sealed class CharacterEntry
    {
        public int CharacterId;
        public bool IsSpawned;
        public bool IsConnected;
    }
}
