using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum MazeAction
{
    MoveForward,
    MoveBackward,
    MoveLeft,
    MoveRight,
    Jump,
    Transform,
    UseItem,
    Mark,
    GodView,
    CheatView,
    Pause,
    ToggleCursor,
    Shoot
}

public static class MazeKeybinds
{
    private static readonly MazeAction[] Actions = (MazeAction[])Enum.GetValues(typeof(MazeAction));
    private static readonly Key[] Defaults =
    {
        Key.W, Key.S, Key.A, Key.D, Key.Space, Key.F, Key.E, Key.Digit1,
        Key.Q, Key.M, Key.Escape, Key.Tab, Key.J
    };
    private static Key[] _bindings;
    private static bool _loaded;

    public static int ActionCount => Actions.Length;
    public static MazeAction GetActionAt(int index) => Actions[index];

    public static Key GetBinding(MazeAction action)
    {
        EnsureLoaded();
        return _bindings[(int)action];
    }

    public static bool IsPressed(MazeAction action)
    {
        Key key = GetBinding(action);
        if (key == Key.None) return false;
        if (Keyboard.current != null) return Keyboard.current[key].isPressed;
        return Input.GetKey(ToKeyCode(key));
    }

    public static bool WasPressed(MazeAction action)
    {
        Key key = GetBinding(action);
        if (key == Key.None) return false;
        if (Keyboard.current != null) return Keyboard.current[key].wasPressedThisFrame;
        return Input.GetKeyDown(ToKeyCode(key));
    }

    public static void SetBinding(MazeAction action, Key key)
    {
        if (key == Key.None) return;
        EnsureLoaded();
        int index = (int)action;
        Key previous = _bindings[index];
        for (int i = 0; i < _bindings.Length; i++)
        {
            if (i == index || _bindings[i] != key) continue;
            _bindings[i] = previous;
            SaveBinding(i);
        }
        _bindings[index] = key;
        SaveBinding(index);
        PlayerPrefs.Save();
    }

    public static void ResetDefaults()
    {
        EnsureLoaded();
        for (int i = 0; i < Defaults.Length; i++)
        {
            _bindings[i] = Defaults[i];
            SaveBinding(i);
        }
        PlayerPrefs.Save();
    }

    public static bool TryReadPressedKey(out Key key)
    {
        key = Key.None;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return false;
        Array values = Enum.GetValues(typeof(Key));
        for (int i = 0; i < values.Length; i++)
        {
            Key candidate = (Key)values.GetValue(i);
            if (candidate == Key.None || !keyboard[candidate].wasPressedThisFrame) continue;
            key = candidate;
            return true;
        }
        return false;
    }

    public static string GetKeyLabel(Key key)
    {
        switch (key)
        {
            case Key.None: return "未绑定";
            case Key.Space: return "空格";
            case Key.Escape: return "Esc";
            case Key.Digit1: return "1";
            case Key.Digit2: return "2";
            case Key.Digit3: return "3";
            case Key.UpArrow: return "↑";
            case Key.DownArrow: return "↓";
            case Key.LeftArrow: return "←";
            case Key.RightArrow: return "→";
            default: return key.ToString();
        }
    }

    public static string GetActionLabel(MazeAction action)
    {
        switch (action)
        {
            case MazeAction.MoveForward: return "向前移动";
            case MazeAction.MoveBackward: return "向后移动";
            case MazeAction.MoveLeft: return "向左移动";
            case MazeAction.MoveRight: return "向右移动";
            case MazeAction.Jump: return "跳跃";
            case MazeAction.Transform: return "变形";
            case MazeAction.UseItem: return "使用道具";
            case MazeAction.Mark: return "脚下标记";
            case MazeAction.GodView: return "限时俯视";
            case MazeAction.CheatView: return "透视模式";
            case MazeAction.Pause: return "返回主菜单";
            case MazeAction.ToggleCursor: return "显示/锁定鼠标";
            case MazeAction.Shoot: return "射击备用键";
            default: return action.ToString();
        }
    }

    static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _bindings = new Key[Defaults.Length];
        for (int i = 0; i < _bindings.Length; i++)
        {
            int saved = PlayerPrefs.GetInt("maze.keybind." + i, (int)Defaults[i]);
            Key key = (Key)saved;
            _bindings[i] = Enum.IsDefined(typeof(Key), key) ? key : Defaults[i];
        }
    }

    static void SaveBinding(int index)
    {
        PlayerPrefs.SetInt("maze.keybind." + index, (int)_bindings[index]);
    }

    static KeyCode ToKeyCode(Key key)
    {
        int keyValue = (int)key;
        int firstDigit = (int)Key.Digit0;
        if (keyValue >= firstDigit && keyValue <= (int)Key.Digit9)
            return (KeyCode)((int)KeyCode.Alpha0 + keyValue - firstDigit);
        if (Enum.TryParse(key.ToString(), true, out KeyCode keyCode)) return keyCode;
        return KeyCode.None;
    }
}
