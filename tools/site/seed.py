"""Демо-данные для снимков сайта — в портативную копию приложения.

    python seed.py <папка приложения> <папка моделей>
    python seed.py <папка приложения> --theme Dark

Копия с portable.txt хранит настройки, звонки и историю в своей папке Data,
поэтому живой профиль (%APPDATA%\\Tapybara) и настоящие звонки не трогаются
вовсе: снимок для сайта — не повод переписывать чужие данные. Модели берутся
из настоящей папки только на чтение, чтобы страница моделей показывала
скачанные.

Звонок «Marina, Kirill, Dana» — тот же разговор, что в демонстрации на сайте.
"""
import json
import math
import os
import shutil
import struct
import sys
from datetime import datetime, timedelta

APP = os.path.abspath(sys.argv[1])
DATA = os.path.join(APP, 'Data')
CALLS = os.path.join(DATA, 'Calls')
SETTINGS = os.path.join(DATA, 'settings.json')


def vec(*pairs, n=16):
    v = [0.0] * n
    for i, x in pairs:
        v[i] = x
    norm = math.sqrt(sum(x * x for x in v))
    return [x / norm for x in v]


def ts(sec):
    return "%02d:%02d:%02d" % (sec // 3600, sec % 3600 // 60, sec % 60)


def stamp(at):
    return at.strftime('%Y-%m-%dT%H:%M:%S') + '+03:00'


def wav(path, seconds):
    """Тишина нужной длины: звонок должен выглядеть записанным, звук не нужен."""
    data = int(seconds) * 32000
    with open(path, 'wb') as f:
        f.write(b'RIFF' + struct.pack('<I', 36 + data) + b'WAVEfmt ' +
                struct.pack('<IHHIIHH', 16, 1, 1, 16000, 32000, 2, 16) + b'data' + struct.pack('<I', data))
        f.truncate(44 + data)


def call(start, trigger, minutes, participants, lines, voices=(), names=None, title=None, prints=None):
    folder = os.path.join(CALLS, start.strftime('%Y-%m-%d %H-%M-%S') + f' ({trigger})')
    os.makedirs(folder, exist_ok=True)
    meta = {
        "Directory": folder,
        "StartedAt": stamp(start),
        "Trigger": trigger,
        "Duration": ts(int(minutes * 60)),
        "Participants": list(participants),
        "Voices": list(voices),
        "VoiceNames": names or {},
    }
    if title:
        meta["Title"] = title
    json.dump(meta, open(os.path.join(folder, 'meta.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=2)

    out = []
    for t, channel, voice, text in lines:
        line = {"Channel": channel, "Start": ts(int(t)), "End": ts(int(t + 2 + len(text) / 16)), "Text": text}
        if voice:
            line["Voice"] = voice
        out.append(line)

    # Сверка реплик уже «прошла»: иначе приложение сверило бы их с тишиной
    # вместо голосов и засомневалось бы в каждой.
    transcript = {"Version": 1, "Lines": out, "VoicesSplit": len(voices) > 1, "VoicesChecked": True,
                  "ExpectedVoices": len(participants), "VoicePrints": prints or {}}
    json.dump(transcript, open(os.path.join(folder, 'transcript.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    open(os.path.join(folder, 'transcript.md'), 'w', encoding='utf-8').write('# Call\n')
    wav(os.path.join(folder, 'mic.wav'), minutes * 60)
    wav(os.path.join(folder, 'system.wav'), minutes * 60)


def seed(models):
    shutil.rmtree(DATA, ignore_errors=True)
    os.makedirs(CALLS)
    open(os.path.join(APP, 'portable.txt'), 'w').close()

    json.dump({
        "UiLanguage": "en", "Theme": "Dark", "MyName": "Me", "ModelsDirectory": models,
        "RememberVoices": True, "KeepDictationHistory": True, "OnboardingDone": True, "SplitVoices": True,
        "CheckForUpdates": False,
        "KnownParticipants": ["Marina", "Kirill", "Dana", "Olga", "Tim"],
        "Replacements": {
            "hugging face": "Hugging Face", "hagging face": "Hugging Face", "hacking face": "Hugging Face",
            "see sharp": "C#", "c sharp": "C#",
            "dot net": ".NET", "dotnet": ".NET",
            "postgres QL": "PostgreSQL", "post gress": "PostgreSQL", "postgre": "PostgreSQL",
            "cooper netties": "Kubernetes", "kuber nettis": "Kubernetes",
            "tappy bara": "Tapybara", "tap a bara": "Tapybara",
            "fig ma": "Figma",
        },
    }, open(SETTINGS, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)

    now = datetime.now().replace(second=0, microsecond=0)
    today = now.replace(hour=0, minute=0)
    json.dump({
        "Kirill": {"Prints": [vec((0, 1.0)), vec((0, 1.0), (7, 0.3))], "Updated": stamp(now)},
        "Marina": {"Prints": [vec((2, 1.0))], "Updated": stamp(now)},
        "Olga": {"Prints": [vec((5, 1.0))], "Updated": stamp(now)},
    }, open(os.path.join(DATA, 'voices.json'), 'w', encoding='utf-8'), indent=2)

    # Звонок из демонстрации на сайте. Кирилла разделитель развалил на два
    # голоса, B и D, и оба уже названы им — это один человек; Дана ждёт имени.
    demo = [
        (3.4, "Mine", None, "…so where are we on the export?"),
        (7.1, "Theirs", "B", "Ready by Thursday, I think."),
        (11.0, "Theirs", "A", "Not from my side — the review is still open."),
        (15.6, "Mine", None, "Right. What is blocking the review?"),
        (19.2, "Theirs", "A", "Two of the fixtures are wrong, and I want Dana to look before we merge."),
        (25.4, "Theirs", "C", "I can take it this afternoon."),
        (28.8, "Theirs", "B", "Then Friday morning is realistic, not Thursday."),
        (33.0, "Mine", None, "Friday it is. I will move the design review to match."),
        (36.9, "Theirs", "C", "Works for me."),
        (38.5, "Theirs", "A", "Same. I will post the fixture list after lunch."),
        (44.0, "Theirs", "D", "One more thing — the Kubernetes rollout waits for the PostgreSQL migration."),
        (51.2, "Mine", None, "Understood. Let's check on it Monday."),
    ]
    call(today + timedelta(hours=11, minutes=2), "Zoom", 23.2, ["Marina", "Kirill", "Dana"], demo,
         voices=("A", "B", "C", "D"), names={"A": "Marina", "B": "Kirill", "D": "Kirill"},
         prints={"A": vec((2, 1.0)), "B": vec((0, 0.87), (1, 0.49)), "C": vec((9, 1.0)), "D": vec((0, 0.8), (3, 0.6))})

    call(today + timedelta(hours=9, minutes=30), "Teams", 48.3, ["Olga", "Tim"], [
        (2, "Theirs", "A", "Let's start with the onboarding flow."),
        (9, "Theirs", "B", "I have the new screens ready."),
        (15, "Mine", None, "Great, share them."),
    ], voices=("A", "B"), names={"A": "Olga", "B": "Tim"}, title="Design review")

    # Незнакомое слово, искажённое по-разному, — для карточки правки слова.
    call(today - timedelta(days=1) + timedelta(hours=16), "Zoom", 31.7, ["Marina", "Kirill", "Dana", "Olga"], [
        (2, "Theirs", "A", "Weekly sync, everyone here?"),
        (6, "Mine", None, "Yes, let's go. Where are we with the Kubernetis rollout?"),
        (12, "Theirs", "B", "It waits for the database migration, as planned."),
        (18, "Mine", None, "And the Kubernates dashboards?"),
        (24, "Theirs", "C", "Kubernetis dashboards are done, the alerts are next."),
        (31, "Theirs", "D", "I will write up the Kubernetus runbook this week."),
        (38, "Mine", None, "Good. The Kubernetes part is settled, then."),
    ], voices=("A", "B", "C", "D"), names={"A": "Marina", "B": "Kirill", "C": "Dana", "D": "Olga"}, title="Weekly sync")

    call(today - timedelta(days=1) + timedelta(hours=11, minutes=15), "Telegram", 12.1, ["Kirill"], [
        (1, "Theirs", None, "Got a minute about the export?"),
        (4, "Mine", None, "Sure."),
    ])

    call(today - timedelta(days=2) + timedelta(hours=14), "Google Meet", 55.0, ["Tim"], [
        (1, "Mine", None, "Thanks for joining, Tim."),
        (5, "Theirs", None, "Happy to be here."),
    ], title="Backend interview")

    call(today - timedelta(days=5) + timedelta(hours=10, minutes=30), "Zoom", 8.7, ["Dana"], [
        (1, "Theirs", None, "Quick question about the fixtures."),
    ])

    dictations = [
        (0, 16, 42, "Can you move the design review to Friday morning? Thursday is not going to work for Dana, and I would rather have everyone in the room."),
        (0, 15, 18, "Note to self: check why the export job retries three times before failing. It looks like the timeout is shorter than the upload itself."),
        (0, 14, 5, "Thanks, merged. Let's keep the fixture list in the repository so the next person does not have to ask."),
        (0, 11, 40, "The installer now asks for the models folder only once and remembers the answer across updates, so a reinstall no longer loses downloaded models."),
        (0, 10, 12, "Remind me to send the release notes to Marina before lunch."),
        (1, 18, 20, "Draft reply: Friday works for us. I will send the agenda tomorrow morning."),
        (1, 16, 55, "The Kubernetes rollout is paused until the PostgreSQL migration finishes. I will post in the channel when it is done."),
        (1, 12, 30, "Order more coffee for the office — the good one, not the one from last month."),
        (1, 9, 47, "Stand-up: yesterday I finished the voice panel, today I am on the dictionary export, no blockers."),
    ]
    with open(os.path.join(DATA, 'dictations.jsonl'), 'w', encoding='utf-8') as f:
        for days, h, m, text in reversed(dictations):
            at = (today - timedelta(days=days)).replace(hour=h, minute=m)
            f.write(json.dumps({"At": stamp(at), "Text": text}, ensure_ascii=False) + "\n")
    print('seeded', DATA)


def theme(name):
    s = json.load(open(SETTINGS, encoding='utf-8'))
    s["Theme"] = name
    json.dump(s, open(SETTINGS, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)


if sys.argv[2] == '--theme':
    theme(sys.argv[3])
else:
    seed(os.path.abspath(sys.argv[2]))
