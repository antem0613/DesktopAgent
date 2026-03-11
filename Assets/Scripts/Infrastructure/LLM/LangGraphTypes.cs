using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// LangGraph REST の入出力型定義
/// </summary>
[Serializable]
public sealed class LangGraphRequest
{
    [JsonProperty("message")]
    public string Message;
}

/// <summary>
/// LangGraph REST の標準レスポンス型定義
/// </summary>
[Serializable]
public sealed class LangGraphResponse
{
    [JsonProperty("message")]
    public string Message;
}
