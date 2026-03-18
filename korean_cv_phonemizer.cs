using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;

namespace OpenUtau.Plugin.KoreanCV
{
    [Phonemizer("Korean C+V Phonemizer Beta", "KO C+V", language: "KO")]
    public class KoreanCVPhonemizer : BaseKoreanPhonemizer
    {
        private const int HangulSyllableStart = 0xAC00;
        private const int HangulSyllableEnd = 0xD7A3;

        // Unicode order for complete Hangul syllable decomposition.
        private static readonly char[] InitialJamoOrder =
        {
            'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ',
            'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ',
        };

        private static readonly char[] MedialJamoOrder =
        {
            'ㅏ', 'ㅐ', 'ㅑ', 'ㅒ', 'ㅓ', 'ㅔ', 'ㅕ', 'ㅖ', 'ㅗ', 'ㅘ',
            'ㅙ', 'ㅚ', 'ㅛ', 'ㅜ', 'ㅝ', 'ㅞ', 'ㅟ', 'ㅠ', 'ㅡ', 'ㅢ', 'ㅣ',
        };

        private static readonly string[] FinalJamoOrder =
        {
            "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ", "ㄺ",
            "ㄻ", "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ", "ㅄ",
            "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ",
        };

        // 자음 매핑 (초성)
        private static readonly Dictionary<char, string> initialConsonants = new()
        {
            ['ㄱ'] = "- g", ['ㄴ'] = "- n", ['ㄷ'] = "- d", ['ㄹ'] = "- r", ['ㅁ'] = "- m",
            ['ㅂ'] = "- b", ['ㅅ'] = "- s", ['ㅇ'] = "-", ['ㅈ'] = "- j", ['ㅊ'] = "- ch",
            ['ㅋ'] = "- k", ['ㅌ'] = "- t", ['ㅍ'] = "- p", ['ㅎ'] = "- h",
            ['ㄲ'] = "- kk", ['ㄸ'] = "- tt", ['ㅃ'] = "- pp", ['ㅆ'] = "- ss", ['ㅉ'] = "- jj"
        };

        // 단모음 매핑
        private static readonly Dictionary<char, string> vowels = new()
        {
            ['ㅏ'] = "- a", ['ㅓ'] = "- eo", ['ㅗ'] = "- o", ['ㅜ'] = "- u",
            ['ㅡ'] = "- eu", ['ㅣ'] = "- i", ['ㅔ'] = "- e", ['ㅐ'] = "- e"
        };

        // 이중모음 매핑
        private static readonly Dictionary<char, string> diphthongs = new()
        {
            ['ㅑ'] = "- ya", ['ㅕ'] = "- yeo", ['ㅛ'] = "- yo", ['ㅠ'] = "- yu",
            ['ㅒ'] = "- yae", ['ㅖ'] = "- ye", ['ㅘ'] = "- wa", ['ㅙ'] = "- we",
            ['ㅚ'] = "- we", ['ㅝ'] = "- wo", ['ㅞ'] = "- we", ['ㅟ'] = "- wi", ['ㅢ'] = "- ui"
        };

        // 받침 매핑 (겹받침 포함)
        private static readonly Dictionary<string, string> finalConsonantsOnly = new(StringComparer.Ordinal)
        {
            ["ㄱ"] = "K", ["ㄲ"] = "K", ["ㄳ"] = "K", ["ㅋ"] = "K", ["ㄺ"] = "K",
            ["ㄴ"] = "N", ["ㄵ"] = "N", ["ㄶ"] = "N",
            ["ㄷ"] = "T", ["ㅅ"] = "T", ["ㅆ"] = "T", ["ㅈ"] = "T", ["ㅊ"] = "T", ["ㅌ"] = "T", ["ㅎ"] = "T",
            ["ㄹ"] = "L", ["ㄼ"] = "L", ["ㄽ"] = "L", ["ㄾ"] = "L", ["ㅀ"] = "L",
            ["ㅁ"] = "M", ["ㄻ"] = "M",
            ["ㅂ"] = "P", ["ㅄ"] = "P", ["ㅍ"] = "P", ["ㄿ"] = "P",
            ["ㅇ"] = "NG",
        };

        public override Result ConvertPhonemes(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours)
        {
            if (notes.Length == 0)
            {
                return new Result { phonemes = Array.Empty<Phoneme>() };
            }

            var note = notes[0];
            var lyric = note.lyric?.Trim() ?? string.Empty;
            int totalDuration = notes.Sum(n => n.duration);

            // 확장자 노트 처리
            if (lyric.StartsWith("+"))
            {
                return new Result { phonemes = Array.Empty<Phoneme>() };
            }

            // 한글 분해
            var decomposed = DecomposeHangul(lyric);

            // 파싱 실패(비한글/미지원 문자) 시 원문 alias를 그대로 찾는다.
            if (decomposed.Count == 0)
            {
                return new Result
                {
                    phonemes = new[]
                    {
                        new Phoneme { index = 0, phoneme = ResolveMappedAlias(lyric, note, 0), position = 0 },
                    },
                };
            }

            var rawPhonemes = new List<Phoneme>();
            int syllableCount = decomposed.Count;
            for (int i = 0; i < syllableCount; i++)
            {
                var syllable = decomposed[i];
                int syllableStart = totalDuration * i / syllableCount;
                int syllableEnd = totalDuration * (i + 1) / syllableCount;
                int syllableDuration = Math.Max(1, syllableEnd - syllableStart);
                rawPhonemes.AddRange(ProcessSyllable(syllable, syllableDuration, syllableStart));
            }

            var mappedPhonemes = new List<Phoneme>(rawPhonemes.Count);
            for (int i = 0; i < rawPhonemes.Count; i++)
            {
                var phoneme = rawPhonemes[i];
                mappedPhonemes.Add(new Phoneme
                {
                    index = i,
                    phoneme = ResolveMappedAlias(phoneme.phoneme, note, i),
                    position = phoneme.position,
                });
            }

            return new Result { phonemes = mappedPhonemes.ToArray() };
        }

        public override Result GenerateEndSound(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours)
        {
            return ConvertPhonemes(notes, prev, next, prevNeighbour, nextNeighbour, prevNeighbours);
        }

        private List<HangulSyllable> DecomposeHangul(string text)
        {
            var result = new List<HangulSyllable>();

            foreach (char c in text)
            {
                if (c >= HangulSyllableStart && c <= HangulSyllableEnd)
                {
                    int unicode = c - HangulSyllableStart;
                    int initial = unicode / (MedialJamoOrder.Length * 28);
                    int medial = (unicode % (MedialJamoOrder.Length * 28)) / 28;
                    int final = unicode % 28;

                    result.Add(new HangulSyllable
                    {
                        Initial = GetInitialConsonant(initial),
                        Medial = GetMedialVowel(medial),
                        Final = final > 0 ? GetFinalConsonant(final - 1) : null
                    });
                }
            }

            return result;
        }

        private List<Phoneme> ProcessSyllable(HangulSyllable syllable, int syllableDuration, int syllableOffset)
        {
            var phonemes = new List<Phoneme>();
            
            // 무성음 자음 기본 길이
            int consonantDuration = 25; 
            
            // 유성 자음(n, r, m)일 경우 타이밍
            if (syllable.Initial == "- n" || syllable.Initial == "- r" || syllable.Initial == "- m")
            {
                consonantDuration = 20;
            }

            if (!string.IsNullOrEmpty(syllable.Initial) && syllable.Initial != "-")
            {
                phonemes.Add(new Phoneme { phoneme = syllable.Initial, position = syllableOffset });
                if (!string.IsNullOrEmpty(syllable.Medial))
                {
                    var vowelOnly = syllable.Medial.Replace("- ", "");
                    int vowelPosition = syllableOffset + Math.Min(consonantDuration, Math.Max(0, syllableDuration - 1));
                    phonemes.Add(new Phoneme { phoneme = vowelOnly, position = vowelPosition });
                }
            }
            else if (!string.IsNullOrEmpty(syllable.Medial))
            {
                phonemes.Add(new Phoneme { phoneme = syllable.Medial, position = syllableOffset });
            }

            if (!string.IsNullOrEmpty(syllable.Final))
            {
                string finalPhoneme = finalConsonantsOnly.TryGetValue(syllable.Final, out var mappedFinal)
                    ? mappedFinal
                    : syllable.Final;
                
                // 받침 기본 위치 설정
                int finalDuration = 40;
                
                // M, L, NG의 경우 타이밍을 짧게 설정
                if (finalPhoneme == "M" || finalPhoneme == "L" || finalPhoneme == "NG")
                {
                    finalDuration = 20; 
                }
                else if (finalPhoneme == "N")
                {
                    finalDuration = 13;
                }

                // 받침 위치를 노트 길이에 따라 동적으로 조정
                int finalPosition = syllableOffset + Math.Max(0, syllableDuration - finalDuration);
                phonemes.Add(new Phoneme { phoneme = finalPhoneme, position = finalPosition });
            }

            return phonemes;
        }

        private string GetInitialConsonant(int index)
        {
            if (index < InitialJamoOrder.Length)
            {
                char initial = InitialJamoOrder[index];
                if (initial == 'ㅇ') return "";
                return initialConsonants.ContainsKey(initial) ? initialConsonants[initial] : "";
            }
            return "";
        }

        private string GetMedialVowel(int index)
        {
            if (index < MedialJamoOrder.Length)
            {
                char vowel = MedialJamoOrder[index];
                if (diphthongs.ContainsKey(vowel)) return diphthongs[vowel];
                if (vowels.ContainsKey(vowel)) return vowels[vowel];
            }
            return "";
        }

        private string GetFinalConsonant(int index)
        {
            return index < FinalJamoOrder.Length ? FinalJamoOrder[index] : null;
        }

        private string ResolveMappedAlias(string phoneme, Note note, int phonemeIndex)
        {
            if (string.IsNullOrWhiteSpace(phoneme))
            {
                return phoneme;
            }
            if (singer == null)
            {
                return phoneme;
            }

            var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == phonemeIndex) ?? default;
            string alt = attr.alternate?.ToString() ?? string.Empty;
            string color = attr.voiceColor ?? string.Empty;
            int shiftedTone = note.tone + attr.toneShift;

            if (singer.TryGetMappedOto(phoneme + alt, shiftedTone, color, out var mappedWithAlt))
            {
                return mappedWithAlt.Alias;
            }
            if (singer.TryGetMappedOto(phoneme, shiftedTone, color, out var mapped))
            {
                return mapped.Alias;
            }
            if (attr.toneShift != 0)
            {
                if (singer.TryGetMappedOto(phoneme + alt, note.tone, color, out mappedWithAlt))
                {
                    return mappedWithAlt.Alias;
                }
                if (singer.TryGetMappedOto(phoneme, note.tone, color, out mapped))
                {
                    return mapped.Alias;
                }
            }
            if (singer.TryGetMappedOto(phoneme + alt, shiftedTone, out mappedWithAlt))
            {
                return mappedWithAlt.Alias;
            }
            if (singer.TryGetMappedOto(phoneme, shiftedTone, out mapped))
            {
                return mapped.Alias;
            }

            return phoneme;
        }
    }

    public class HangulSyllable
    {
        public string Initial { get; set; } = string.Empty;
        public string Medial { get; set; } = string.Empty;
        public string Final { get; set; }
    }
}
