using UnityEngine;

public class PrefabStressTest : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField]
    private GameObject _defaultPrefab;

    [SerializeField]
    private GameObject _arrayPrefab;

    [Header("Grid Settings")]
    [SerializeField]
    private int _prefabPerRow = 10;

    [SerializeField]
    private int _count = 500;

    [SerializeField]
    private float _spacing = 1.5f;

    private Transform _root;

    [ContextMenu("Spawn Default")]
    public void SpawnDefault()
    {
        Spawn(_defaultPrefab, "Default Cubes");
    }

    [ContextMenu("Spawn Array")]
    public void SpawnArray()
    {
        Spawn(_arrayPrefab, "Array Cubes");
    }

    private void Spawn(GameObject prefab, string rootName)
    {
        if (prefab == null)
        {
            Debug.LogError("Prefab not assigned");
            return;
        }

        Clear();
        _root = new GameObject(rootName).transform;

        for (int i = 0; i < _count; i++)
        {
            Vector3 pos = GetPosition(i);
            Instantiate(prefab, pos, Quaternion.identity, _root);
        }
    }

    private void Clear()
    {
        if (_root != null)
            Destroy(_root.gameObject);
    }

    private Vector3 GetPosition(int index)
    {
        int col = index % _prefabPerRow;
        int row = index / _prefabPerRow;

        return new Vector3(
            col * _spacing,
            0,
            row * _spacing
        );
    }
}