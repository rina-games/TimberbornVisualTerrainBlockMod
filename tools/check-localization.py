#!/usr/bin/env python3
"""校验 Mod 本地化 CSV：每行必须是 3 列（ID,Text,Comment）。

游戏里的 LocalizationCsvValidator 遇到多余逗号会直接抛异常并让游戏启动失败
（InvalidDataException: Unnecessary comma found in <lang> (line N)），
所以改文案后先跑这个脚本再进游戏。

用法： python3 tools/check-localization.py
"""
import csv
import glob
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOC_DIR = os.path.join(ROOT, "Localizations")


def main():
    files = sorted(glob.glob(os.path.join(LOC_DIR, "*.csv")))
    if not files:
        print("没有找到 Localizations/*.csv")
        return 1

    failed = False
    for path in files:
        name = os.path.basename(path)
        with open(path, encoding="utf-8-sig") as handle:
            for line_no, row in enumerate(csv.reader(handle), start=1):
                if not row:
                    continue
                if len(row) != 3:
                    print("%s 第 %d 行有 %d 列（应为 3）：%r" % (name, line_no, len(row), row))
                    failed = True
        if not failed:
            print("%s OK" % name)

    if failed:
        print("\n文案里有逗号时整段必须用双引号包起来，例如：ID,\"text, with comma\",-")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
