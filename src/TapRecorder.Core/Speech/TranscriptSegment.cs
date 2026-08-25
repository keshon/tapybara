namespace TapRecorder.Core.Speech;

/// <summary>
/// Кусок распознанной речи с таймингами. Для диктовки достаточно склеить
/// <see cref="Text"/> всех сегментов; для звонка тайминги нужны, чтобы
/// свести реплики двух каналов в один хронологический транскрипт.
/// </summary>
/// <param name="Start">Смещение от начала записи.</param>
/// <param name="End">Конец фрагмента.</param>
/// <param name="Text">Текст фрагмента (whisper отдаёт его с ведущим пробелом — здесь уже обрезан).</param>
public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);
