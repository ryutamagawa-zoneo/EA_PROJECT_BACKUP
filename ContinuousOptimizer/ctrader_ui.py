"""
cTrader GUI 自動操作モジュール
pyautogui でクリック・キー操作、pygetwindow でウィンドウ管理

使用前に setup_coords.py で UI 座標をキャリブレーションすること。
"""

import json
import os
import time
import sys

try:
    import pyautogui
    pyautogui.FAILSAFE = True   # 左上端にマウスを持っていくと緊急停止
    pyautogui.PAUSE = 0.3       # 各操作後の待機時間
except ImportError:
    pyautogui = None

try:
    import pygetwindow as gw
except ImportError:
    gw = None


# ──────────────────────────────────────────────
# UI座標定義
# ──────────────────────────────────────────────

DEFAULT_COORDS = {
    "_comment": "cTrader UI の各ボタン・入力欄の座標。setup_coords.py で設定してください。",

    "ctrader_window_title": "cTrader",

    "backtest_tab": [0, 0],
    "bot_settings_gear": [0, 0],
    "import_settings_btn": [0, 0],
    "file_dialog_path_bar": [0, 0],
    "file_dialog_open_btn": [0, 0],
    "backtest_play_btn": [0, 0],
    "backtest_stop_btn": [0, 0],

    "result_net_profit_area": [0, 0, 200, 30],
    "result_max_dd_area": [0, 0, 200, 30],
}


def load_ui_coords(path: str) -> dict:
    """UI座標設定を読み込む"""
    if not os.path.isfile(path):
        print(f"[WARN] UI座標ファイルが見つかりません: {path}")
        print(f"       setup_coords.py を実行してください。")
        return DEFAULT_COORDS

    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def save_ui_coords(coords: dict, path: str) -> None:
    """UI座標設定を保存"""
    with open(path, "w", encoding="utf-8") as f:
        json.dump(coords, f, ensure_ascii=False, indent=2)


# ──────────────────────────────────────────────
# ウィンドウ管理
# ──────────────────────────────────────────────

def find_ctrader_window(title_keyword: str = "cTrader"):
    """cTraderウィンドウを取得"""
    if gw is None:
        print("[ERROR] pygetwindow がインストールされていません")
        return None

    windows = gw.getWindowsWithTitle(title_keyword)
    if not windows:
        print(f"[WARN] '{title_keyword}' ウィンドウが見つかりません")
        return None
    return windows[0]


def activate_ctrader(title_keyword: str = "cTrader") -> bool:
    """cTraderウィンドウを最前面に"""
    win = find_ctrader_window(title_keyword)
    if win is None:
        return False
    try:
        if win.isMinimized:
            win.restore()
        win.activate()
        time.sleep(0.5)
        return True
    except Exception as e:
        print(f"[WARN] ウィンドウ操作エラー: {e}")
        return False


# ──────────────────────────────────────────────
# クリック操作
# ──────────────────────────────────────────────

def safe_click(coord, description: str = "", pause: float = 0.5) -> bool:
    """座標をクリック（安全チェック付き）"""
    if pyautogui is None:
        print("[ERROR] pyautogui がインストールされていません")
        return False

    x, y = coord[0], coord[1]
    if x == 0 and y == 0:
        print(f"[SKIP] 座標が未設定: {description}")
        return False

    if description:
        print(f"  Click: {description} ({x}, {y})")

    pyautogui.click(x, y)
    time.sleep(pause)
    return True


def safe_type(text: str, interval: float = 0.02) -> None:
    """テキストを入力"""
    if pyautogui is None:
        return
    pyautogui.typewrite(text, interval=interval)


# ──────────────────────────────────────────────
# cTrader 操作シーケンス
# ──────────────────────────────────────────────

class CTraderController:
    """cTrader の GUI 操作を管理"""

    def __init__(self, coords_path: str = "ui_coords.json"):
        self.coords = load_ui_coords(coords_path)
        self.window_title = self.coords.get("ctrader_window_title", "cTrader")

    def activate(self) -> bool:
        """cTraderを最前面に"""
        return activate_ctrader(self.window_title)

    def import_cbotset(self, cbotset_path: str) -> bool:
        """
        .cbotset ファイルをインポート

        手順:
        1. 設定ギアアイコンをクリック
        2. Import ボタンをクリック
        3. ファイルダイアログでパスを入力
        4. Open をクリック
        """
        if not self.activate():
            return False

        print(f"\n[UI] cbotset インポート: {cbotset_path}")

        # 設定ギアアイコン
        if not safe_click(self.coords.get("bot_settings_gear"), "設定ギア"):
            return False
        time.sleep(0.5)

        # Import ボタン
        if not safe_click(self.coords.get("import_settings_btn"), "Import"):
            return False
        time.sleep(1.0)

        # ファイルダイアログ: パスバーにフォーカス
        path_bar = self.coords.get("file_dialog_path_bar")
        if path_bar and path_bar[0] > 0:
            safe_click(path_bar, "パスバー")
            time.sleep(0.3)

        # ファイルパスを入力
        # Windowsのファイルダイアログでは、パスバーに直接入力できる
        if pyautogui:
            pyautogui.hotkey("ctrl", "l")  # パスバーにフォーカス
            time.sleep(0.3)
            # 日本語パスに対応するため、クリップボード経由で入力
            import subprocess
            abs_path = os.path.abspath(cbotset_path)
            subprocess.run(
                ["clip"],
                input=abs_path.encode("utf-16le"),
                check=True
            )
            pyautogui.hotkey("ctrl", "v")
            time.sleep(0.5)
            pyautogui.press("enter")
            time.sleep(1.0)

        return True

    def start_backtest(self) -> bool:
        """バックテストを開始"""
        if not self.activate():
            return False

        print("[UI] バックテスト開始")
        return safe_click(self.coords.get("backtest_play_btn"), "Play ボタン", pause=1.0)

    def stop_backtest(self) -> bool:
        """バックテストを停止"""
        if not self.activate():
            return False

        print("[UI] バックテスト停止")
        return safe_click(self.coords.get("backtest_stop_btn"), "Stop ボタン", pause=0.5)

    def run_backtest_cycle(self, cbotset_path: str,
                           pro_report_folder: str,
                           timeout_seconds: int = 600,
                           poll_seconds: float = 5.0) -> bool:
        """
        1回のバックテストサイクル:
        1. cbotset インポート
        2. バックテスト実行
        3. PRO レポート出力を待機
        """
        # 現在のファイルリストを記録
        import glob
        before_files = set(
            glob.glob(os.path.join(pro_report_folder, "**/PRO_*.html"), recursive=True)
        )

        # cbotset インポート
        if not self.import_cbotset(cbotset_path):
            print("[ERROR] cbotset インポート失敗")
            return False

        time.sleep(1.0)

        # バックテスト開始
        if not self.start_backtest():
            print("[ERROR] バックテスト開始失敗")
            return False

        # PRO レポート出力を待機
        print(f"[UI] バックテスト完了待機中... (最大 {timeout_seconds}秒)")
        start_time = time.time()

        while time.time() - start_time < timeout_seconds:
            time.sleep(poll_seconds)

            current_files = set(
                glob.glob(os.path.join(pro_report_folder, "**/PRO_*.html"), recursive=True)
            )
            new_files = current_files - before_files

            if new_files:
                elapsed = time.time() - start_time
                print(f"[UI] 新しい PRO レポート検出 ({elapsed:.0f}秒)")
                for f in new_files:
                    print(f"     -> {f}")
                return True

            # 進捗表示
            elapsed = time.time() - start_time
            if int(elapsed) % 30 == 0 and int(elapsed) > 0:
                print(f"     ... {elapsed:.0f}秒経過")

        print(f"[TIMEOUT] {timeout_seconds}秒経過しても PRO レポートが出力されませんでした")
        return False


# ──────────────────────────────────────────────
# CLI テスト
# ──────────────────────────────────────────────
if __name__ == "__main__":
    ctrl = CTraderController()
    print("=== cTrader UI Controller ===")
    print(f"  ウィンドウタイトル: {ctrl.window_title}")
    print(f"  座標設定:")
    for key, val in ctrl.coords.items():
        if key.startswith("_"):
            continue
        print(f"    {key}: {val}")

    if activate_ctrader():
        print("\n[OK] cTrader ウィンドウをアクティブ化しました")
    else:
        print("\n[INFO] cTrader が起動していないか、見つかりません")
