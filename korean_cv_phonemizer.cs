using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;

namespace OpenUtau.Plugin.KoreanCV
{
    [Phonemizer("Korean C+V Phonemizer Beta", "KO C+V", language: "KO")]
    public class KoreanCVPhonemizer : BaseKoreanPhonemizer
    {
        // 자음 매핑 (초성)
        private static readonly Dictionary<char, string> initialConsonants = new Dictionary<char, string>
        {
            ['ㄱ'] = "- g", ['ㄴ'] = "- n", ['ㄷ'] = "- d", ['ㄹ'] = "- r", ['ㅁ'] = "- m",
            ['ㅂ'] = "- b", ['ㅅ'] = "- s", ['ㅇ'] = "-", ['ㅈ'] = "- j", ['ㅊ'] = "- ch",
            ['ㅋ'] = "- k", ['ㅌ'] = "- t", ['ㅍ'] = "- p", ['ㅎ'] = "- h",
            ['ㄲ'] = "- kk", ['ㄸ'] = "- tt", ['ㅃ'] = "- pp", ['ㅆ'] = "- ss", ['ㅉ'] = "- jj"
        };

        // 단모음 매핑
        private static readonly Dictionary<char, string> vowels = new Dictionary<char, string>
        {
            ['ㅏ'] = "- a", ['ㅓ'] = "- eo", ['ㅗ'] = "- o", ['ㅜ'] = "- u",
            ['ㅡ'] = "- eu", ['ㅣ'] = "- i", ['ㅔ'] = "- e", ['ㅐ'] = "- e"
        };

        // 이중모음 매핑
        private static readonly Dictionary<char, string> diphthongs = new Dictionary<char, string>
        {
            ['ㅑ'] = "- ya", ['ㅕ'] = "- yeo", ['ㅛ'] = "- yo", ['ㅠ'] = "- yu",
            ['ㅒ'] = "- yae", ['ㅖ'] = "- ye", ['ㅘ'] = "- wa", ['ㅙ'] = "- we",
            ['ㅚ'] = "- we", ['ㅝ'] = "- wo", ['ㅞ'] = "- we", ['ㅟ'] = "- wi", ['ㅢ'] = "- ui"
        };

        // 받침 매핑 - 단독 자음
        private static readonly Dictionary<char, string> finalConsonantsOnly = new Dictionary<char, string>
        {
            ['ㄱ'] = "K", ['ㄲ'] = "K", ['ㅋ'] = "K",
            ['ㄴ'] = "N", ['ㄷ'] = "T", ['ㅅ'] = "T", ['ㅆ'] = "T", 
            ['ㅈ'] = "T", ['ㅊ'] = "T", ['ㅌ'] = "T", ['ㄹ'] = "L",
            ['ㅁ'] = "M", ['ㅂ'] = "P", ['ㅍ'] = "P", ['ㅇ'] = "NG"
        };

        // 받침 매핑 - VC 조합
        private static readonly Dictionary<char, string> finalConsonants = new Dictionary<char, string>
        {
            ['ㄱ'] = "a K", ['ㄲ'] = "a K", ['ㅋ'] = "a K",
            ['ㄴ'] = "a N", ['ㄷ'] = "a T", ['ㅅ'] = "a T", ['ㅆ'] = "a T",
            ['ㅈ'] = "a T", ['ㅊ'] = "a T", ['ㅌ'] = "a T", ['ㄹ'] = "a L",
            ['ㅁ'] = "a M", ['ㅂ'] = "a P", ['ㅍ'] = "a P", ['ㅇ'] = "a NG"
        };

        public override Result ConvertPhonemes(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours)
        {
            var phonemes = new List<Phoneme>();
            var note = notes[0];
            var lyric = note.lyric;
            int totalDuration = notes.Sum(n => n.duration);

            // 확장자 노트 처리
            if (lyric.StartsWith("+"))
            {
                return new Result { phonemes = new Phoneme[0] };
            }

            // 한글 분해
            var decomposed = DecomposeHangul(lyric);
            
            for (int i = 0; i < decomposed.Count; i++)
            {
                var syllable = decomposed[i];
                var syllablePhonemes = ProcessSyllable(syllable, totalDuration);
                
                foreach (var ph in syllablePhonemes)
                {
                    // BaseKoreanPhonemizer.FindInOto로 톤, 컬러 자동 처리
                    string mappedPhoneme = FindInOto(singer, ph.phoneme, note);
                    phonemes.Add(new Phoneme { phoneme = mappedPhoneme, position = ph.position });
                }
            }

            return new Result { phonemes = phonemes.ToArray() };
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
                if (c >= '가' && c <= '힣')
                {
                    int unicode = c - '가';
                    int initial = unicode / (21 * 28);
                    int medial = (unicode % (21 * 28)) / 28;
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

        private List<Phoneme> ProcessSyllable(HangulSyllable syllable, int totalDuration)
        {
            var phonemes = new List<Phoneme>();
            int consonantDuration = 30; // 자음 길이를 30틱으로 단축
            int finalPosition = Math.Max(-30, totalDuration - 40); // 받침 위치 조정

            if (!string.IsNullOrEmpty(syllable.Initial) && syllable.Initial != "-")
            {
                phonemes.Add(new Phoneme { phoneme = syllable.Initial, position = 0 });
                if (!string.IsNullOrEmpty(syllable.Medial))
                {
                    var vowelOnly = syllable.Medial.Replace("- ", "");
                    phonemes.Add(new Phoneme { phoneme = vowelOnly, position = consonantDuration });
                }
            }
            else if (!string.IsNullOrEmpty(syllable.Medial))
            {
                phonemes.Add(new Phoneme { phoneme = syllable.Medial, position = 0 });
            }

            if (!string.IsNullOrEmpty(syllable.Final))
            {
                string finalPhoneme = finalConsonantsOnly.ContainsKey(syllable.Final[0]) 
                    ? finalConsonantsOnly[syllable.Final[0]] 
                    : syllable.Final;
                // 받침 위치를 노트 길이에 따라 동적으로 조정
                phonemes.Add(new Phoneme { phoneme = finalPhoneme, position = finalPosition });
            }

            return phonemes;
        }

        private string GetInitialConsonant(int index)
        {
            string[] initials = { "ㄱ", "ㄲ", "ㄴ", "ㄷ", "ㄸ", "ㄹ", "ㅁ", "ㅂ", "ㅃ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅉ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };
            if (index < initials.Length)
            {
                char initial = initials[index][0];
                if (initial == 'ㅇ') return "";
                return initialConsonants.ContainsKey(initial) ? initialConsonants[initial] : "";
            }
            return "";
        }

        private string GetMedialVowel(int index)
        {
            string[] medials = { "ㅏ", "ㅐ", "ㅑ", "ㅒ", "ㅓ", "ㅔ", "ㅕ", "ㅖ", "ㅗ", "ㅘ", "ㅙ", "ㅚ", "ㅛ", "ㅜ", "ㅝ", "ㅞ", "ㅟ", "ㅠ", "ㅡ", "ㅢ", "ㅣ" };
            if (index < medials.Length)
            {
                char vowel = medials[index][0];
                if (diphthongs.ContainsKey(vowel)) return diphthongs[vowel];
                if (vowels.ContainsKey(vowel)) return vowels[vowel];
            }
            return "";
        }

        private string GetFinalConsonant(int index)
        {
            string[] finals = { "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ", "ㄺ", "ㄻ", "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ", "ㅄ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };
            return index < finals.Length ? finals[index] : null;
        }
    }

    public class HangulSyllable
    {
        public string Initial { get; set; }
        public string Medial { get; set; }
        public string Final { get; set; }
    }
}