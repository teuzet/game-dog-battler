using System.Collections.Generic;
using Factory.Shell;
using Game.Core.Modules.Example;
using Game.Core.Sim;
using UnityEngine;

/// <summary>Primitives for the template's example game: a cube per runner,
/// a post per fork, the camera follows participant 0. Replace with the
/// real game's view; it only reads the state.</summary>
public sealed class ExampleRaceView : MonoBehaviour
{
    private readonly List<Transform> _runners = new List<Transform>();
    private Camera _cam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<ExampleRaceView>() == null) new GameObject("ExampleRaceView").AddComponent<ExampleRaceView>();
    }

    private void Start()
    {
        _cam = Camera.main;
        if (_cam == null) _cam = new GameObject("Camera").AddComponent<Camera>();
        if (FactoryGame.Instance != null)
        {
            FactoryGame.Instance.SessionStarted += Build;
            if (FactoryGame.Instance.Sim != null) Build(FactoryGame.Instance.Sim);
        }
    }

    private void Build(Simulation sim)
    {
        foreach (Transform child in transform) Destroy(child.gameObject);
        _runners.Clear();
        var course = sim.Get<Course>(Course.ModuleName);
        foreach (var f in course.Forks)
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            post.SetParent(transform);
            post.position = new Vector3((float)f.ToDouble(), 0.5f, -1f);
            post.localScale = new Vector3(0.2f, 1f, 0.2f);
        }
        for (int p = 0; p < sim.Players; p++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            cube.SetParent(transform);
            cube.GetComponent<Renderer>().material.color = p == FactoryGame.HumanPlayer ? Color.yellow : Color.gray;
            _runners.Add(cube);
        }
    }

    private void LateUpdate()
    {
        var sim = FactoryGame.Instance != null ? FactoryGame.Instance.Sim : null;
        if (sim == null || _runners.Count == 0) return;
        var all = sim.Get<Runners>(Runners.ModuleName).All;
        for (int p = 0; p < all.Length && p < _runners.Count; p++)
            _runners[p].position = new Vector3((float)all[p].Pos.ToDouble(), all[p].StunTicks > 0 ? 0.2f : 0.5f, p * 1.5f);
        if (_cam != null)
        {
            var me = _runners[FactoryGame.HumanPlayer].position;
            _cam.transform.position = me + new Vector3(-6f, 6f, -8f);
            _cam.transform.LookAt(me + new Vector3(6f, 0f, 2f));
        }
    }

    private void OnGUI()
    {
        var sim = FactoryGame.Instance != null ? FactoryGame.Instance.Sim : null;
        if (sim == null) return;
        var me = sim.Get<Runners>(Runners.ModuleName).All[FactoryGame.HumanPlayer];
        GUI.Label(new Rect(16, 16, 600, 30), $"Силы {me.Stamina.FloorToInt()} · Место {me.Place} · Монеты {me.Coins} · D — спринт, A — отдых, W на развилке — верх, R — заново",
                  new GUIStyle(GUI.skin.label) { fontSize = 18 });
    }
}
