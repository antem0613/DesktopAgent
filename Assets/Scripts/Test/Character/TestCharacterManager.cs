using UnityEngine;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

//キャラクターのスポーン、スポーンしたキャラクター一覧を管理
public class TestCharacterManager : MonoBehaviour
{
    [SerializeField] Vector3 spawnPosition; //スポーン位置
    [SerializeField] float spawnRandomRange = 5f; //スポーン位置のランダム範囲
    [SerializeField] GameObject characterPrefab; //スポーンするキャラクタープレハブ

    [SerializeField] int initialCharacterCount = 1; //初期スポーンするキャラクターの数

    [SerializeField] float timeScale = 1f; //ゲーム全体の時間の流れを制御するタイムスケール

    List<TestCompanionCharacter> spawnedCharacters = new List<TestCompanionCharacter>(); //スポーンしたキャラクターのリスト

    async void Start()
    {
        Time.timeScale = timeScale;

        for (int i = 0; i < initialCharacterCount; i++)
        {
            await SpawnCharacter(); //ゲーム開始時にキャラクターをスポーン
        }
    }

    public async UniTask SpawnCharacter()
    {
        //キャラクタープレハブから新しいキャラクターをスポーン
        Vector3 randomOffset = new Vector3(
            Random.Range(-spawnRandomRange, spawnRandomRange), 0, -5f
        );
        GameObject characterObj = Instantiate(characterPrefab, spawnPosition + randomOffset, Quaternion.identity);
        characterObj.AddComponent<TestCompanionCharacter>();
        TestCompanionCharacter character = characterObj.GetComponent<TestCompanionCharacter>();

        if (character != null)
        {
            spawnedCharacters.Add(character); //スポーンしたキャラクターをリストに追加
            TestLog.Info(nameof(TestCharacterManager), $"Character spawned. Total characters: {spawnedCharacters.Count}");
        }
        else
        {
            TestLog.Warning(nameof(TestCharacterManager), "Spawned object does not have TestCompanionCharacter component.");
            Destroy(characterObj); //コンポーネントがない場合はオブジェクトを破棄
        }
    }
}
