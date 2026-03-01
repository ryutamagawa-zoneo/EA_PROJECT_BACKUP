"""
UI座標キャリブレーションツール
cTrader の各ボタン・入力欄の座標を対話的に設定し ui_coords.json に保存

使い方:
  python setup_coords.py

  各ステップで「cTrader の〇〇ボタンの上にマウスを置いてEnterを押してください」
  と表示されるので、マウスを目的の場所に移動してからEnterキーを押す。
"""

import json
import os
import time
import sys

try:
    import pyautogui
except ImportError:
    print("[ERROR] pyautogui がインストールされていません")
    print("  pip install pyautogui")
    sys.exit(1)


COORD_ITEMS = [
    ("ctrader_window_title", "cTrader のウィンドウタイトル (テキスト入力)", "text"),
    ("backtest_tab", "バックテストタブ (Backtesting)", "coord"),
    ("bot_settings_gear", "cBot設定の歯車アイコン (⚙)", "coord"),
    ("import_settings_btn", "設定インポートボタン (Import/Load)", "coord"),
    ("file_dialog_path_bar", "ファイルダイアログのパスバー (省略可)", "coord"),
    ("file_dialog_open_btn", "ファイルダイアログの「開く」ボタン (省略可)", "coord"),
    ("backtest_play_btn", "バックテスト開始ボタン (▶ Play)", "coord"),
    ("backtest_stop_btn", "バックテスト停止ボタン (■ Stop)", "coord"),
]


def capture_coord(label: str) -> list:
    """マウス座標をキャプチャ"""
    print(f"\n  >>> {label}")
    print(f"      マウスをその場所に移動して Enter を押してください（スキップ: s）")

    user_input = input("      > ").strip().lower()
    if user_input == "s":
        print("      [SKIP]")
        return [0, 0]

    x, y = pyautogui.position()
    print(f"      座標: ({x}, {y})")
    return [x, y]


def capture_text(label: str, default: str = "") -> str:
    """テキスト入力"""
    print(f"\n  >>> {label}")
    if default:
        print(f"      デフォルト: {default} (そのまま Enter で使用)")

    user_input = input("      > ").strip()
    return user_input if user_input else default


def main():
    print("=" * 60)
    print("  cTrader UI 座標キャリブレーションツール")
    print("=" * 60)
    print()
    print("  cTrader を開いてバックテスト画面を表示した状態で進めてください。")
    print("  各ステップでマウスを指定の場所に移動してからEnterを押します。")
    print("  スキップしたい項目は 's' と入力してください。")
    print()

    coords = {}

    for key, label, item_type in COORD_ITEMS:
        if item_type == "text":
            coords[key] = capture_text(label, default="cTrader")
        elif item_type == "coord":
            coords[key] = capture_coord(label)

    # 保存
    output_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ui_coords.json")

    print(f"\n{'=' * 60}")
    print(f"  設定結果:")
    for key, val in coords.items():
        print(f"    {key}: {val}")

    print(f"\n  保存先: {output_path}")
    confirm = input("  保存しますか？ (y/n) > ").strip().lower()

    if confirm == "y":
        with open(output_path, "w", encoding="utf-8") as f:
            json.dump(coords, f, ensure_ascii=False, indent=2)
        print(f"\n  [OK] 保存しました: {output_path}")
    else:
        print("  [CANCEL] 保存をキャンセルしました")

    print()
    print("  次のステップ:")
    print("    python main.py --mode auto   (完全自動モード)")
    print("    python main.py --mode watch  (監視モード: 手動バックテスト)")


if __name__ == "__main__":
    main()
