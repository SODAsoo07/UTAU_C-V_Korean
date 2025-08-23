using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.KoreanCV
{
    /// <summary>
    /// Korean C+V Phonemizer
    /// 자음(C)과 모음(V)을 완전히 분리하여 자유롭게 조합하는 순수 C+V 방식
    /// </summary>
    [Phonemizer("Korean C+V Phonemizer Beta", "KO C+V", language: "KO")]
    public class KoreanCVPhonemizer : Phonemizer
    {
        private USinger singer;

        // 자음 매핑 (초성) - 어두 자음은 -접두사 포함한 전체가 하나의 음소
        private static readonly Dictionary<char, string> initialConsonants = new Dictionary<char, string>
        {
            ['ㄱ'] = "- g", ['ㄴ'] = "- n", ['ㄷ'] = "- d", ['ㄹ'] = "- r", ['ㅁ'] = "- m",
            ['ㅂ'] = "- b", ['ㅅ'] = "- s", ['ㅇ'] = "-", ['ㅈ'] = "- j", ['ㅊ'] = "- ch",
            ['ㅋ'] = "- k", ['ㅌ'] = "- t", ['ㅍ'] = "- p", ['ㅎ'] = "- h",
            ['ㄲ'] = "- kk", ['ㄸ'] = "- tt", ['ㅃ'] = "- pp", ['ㅆ'] = "- ss", ['ㅉ'] = "- jj"
        };

        // 자음 매핑 (중간/종성)
        private static readonly Dictionary<char, string> medialConsonants = new Dictionary<char, string>
        {
            ['ㄱ'] = "g", ['ㄴ'] = "n", ['ㄷ'] = "d", ['ㄹ'] = "r", ['ㅁ'] = "m",
            ['ㅂ'] = "b", ['ㅅ'] = "s", ['ㅇ'] = "", ['ㅈ'] = "j", ['ㅊ'] = "ch",
            ['ㅋ'] = "k", ['ㅌ'] = "t", ['ㅍ'] = "p", ['ㅎ'] = "h",
            ['ㄲ'] = "kk", ['ㄸ'] = "tt", ['ㅃ'] = "pp", ['ㅆ'] = "ss", ['ㅉ'] = "jj"
        };

        // 단모음 매핑(초성 단독) - 동음 처리 포함
        private static readonly Dictionary<char, string> vowels = new Dictionary<char, string>
        {
            ['ㅏ'] = "- a", ['ㅓ'] = "- eo", ['ㅗ'] = "- o", ['ㅜ'] = "- u",
            ['ㅡ'] = "- eu", ['ㅣ'] = "- i", ['ㅔ'] = "- e", ['ㅐ'] = "- e" // ㅐ → ㅔ
        };

        // 단모음 매핑(종성 단독)
        private static readonly Dictionary<char, string> vowelsEnding = new Dictionary<char, string>
        {
            ['ㅏ'] = "a -", ['ㅓ'] = "eo -", ['ㅗ'] = "o -", ['ㅜ'] = "u -",
            ['ㅡ'] = "eu -", ['ㅣ'] = "i -", ['ㅔ'] = "e -", ['ㅐ'] = "e -"
        };

        // 이중모음 매핑(초성 단독) - 동음 처리 포함
        private static readonly Dictionary<char, string> diphthongs = new Dictionary<char, string>
        {
            ['ㅑ'] = "- ya", ['ㅕ'] = "- yeo", ['ㅛ'] = "- yo", ['ㅠ'] = "- yu",
            ['ㅒ'] = "- yae", ['ㅖ'] = "- ye", ['ㅘ'] = "- wa", ['ㅙ'] = "- we",
            ['ㅚ'] = "- we", ['ㅝ'] = "- wo", ['ㅞ'] = "- we", ['ㅟ'] = "- wi", ['ㅢ'] = "- ui"
        };

        // 이중모음 매핑(종성 단독)
        private static readonly Dictionary<char, string> diphthongsEnding = new Dictionary<char, string>
        {
            ['ㅑ'] = "ya -", ['ㅕ'] = "yeo -", ['ㅛ'] = "yo -", ['ㅠ'] = "yu -",
            ['ㅒ'] = "yae -", ['ㅖ'] = "ye -", ['ㅘ'] = "wa -", ['ㅙ'] = "we -",
            ['ㅚ'] = "we -", ['ㅝ'] = "wo -", ['ㅞ'] = "we -", ['ㅟ'] = "wi -", ['ㅢ'] = "ui -"
        };

        // 이중모음 대체 조합 (음성파일 없을 시 사용)
        private static readonly Dictionary<char, (string, string, int)> diphthongFallbacks = new Dictionary<char, (string, string, int)>
        {
            ['ㅖ'] = ("i", "e", 30),      // ㅣ(짧게)+ㅔ
            ['ㅢ'] = ("eu", "i", 60),     // ㅡ+ㅣ
            ['ㅘ'] = ("o", "a", 20),      // ㅗ(아주짧게)+ㅏ
            ['ㅟ'] = ("u", "i", 20),      // ㅜ(아주짧게)+ㅣ
            ['ㅝ'] = ("u", "eo", 30)      // ㅜ(짧게)+ㅓ
        };

        // 받침 매핑 - 단독 자음 (C)
        private static readonly Dictionary<char, string> finalConsonantsOnly = new Dictionary<char, string>
        {
            ['ㄱ'] = "K", ['ㄲ'] = "K", ['ㅋ'] = "K",
            ['ㄴ'] = "N",
            ['ㄷ'] = "T", ['ㅅ'] = "T", ['ㅆ'] = "T", ['ㅈ'] = "T", ['ㅊ'] = "T", ['ㅌ'] = "T",
            ['ㄹ'] = "L",
            ['ㅁ'] = "M",
            ['ㅂ'] = "P", ['ㅍ'] = "P",
            ['ㅇ'] = "NG",
            // 겹받침
            ['ㄳ'] = "K", ['ㄵ'] = "N", ['ㄶ'] = "N", ['ㄺ'] = "K", ['ㄻ'] = "M",
            ['ㄼ'] = "L", ['ㄽ'] = "L", ['ㄾ'] = "L", ['ㄿ'] = "P", ['ㅀ'] = "L", ['ㅄ'] = "P"
        };

        // 받침 매핑 - 모음+자음 (VC)
        private static readonly Dictionary<char, string> finalConsonants = new Dictionary<char, string>
        {
            ['ㄱ'] = "a K", ['ㄲ'] = "a K", ['ㅋ'] = "a K",
            ['ㄴ'] = "a N",
            ['ㄷ'] = "a T", ['ㅅ'] = "a T", ['ㅆ'] = "a T", ['ㅈ'] = "a T", ['ㅊ'] = "a T", ['ㅌ'] = "a T",
            ['ㄹ'] = "a L",
            ['ㅁ'] = "a M",
            ['ㅂ'] = "a P", ['ㅍ'] = "a P",
            ['ㅇ'] = "a NG",
            // 겹받침
            ['ㄳ'] = "a K", ['ㄵ'] = "a N", ['ㄶ'] = "a N", ['ㄺ'] = "a K", ['ㄻ'] = "a M",
            ['ㄼ'] = "a L", ['ㄽ'] = "a L", ['ㄾ'] = "a L", ['ㄿ'] = "a P", ['ㅀ'] = "a L", ['ㅄ'] = "a P"
        };

        public override void SetSinger(USinger singer) => this.singer = singer;

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs)
        {
            var phonemes = new List<Phoneme>();
            var note = notes[0];
            var lyric = note.lyric;

            // 음성 힌트 처리 (예: [g a] 형태)
            if (lyric.StartsWith("[") && lyric.EndsWith("]"))
            {
                var phonemeHint = lyric.Substring(1, lyric.Length - 2);
                var hintPhonemes = phonemeHint.Split(' ');
                
                for (int i = 0; i < hintPhonemes.Length; i++)
                {
                    phonemes.Add(new Phoneme
                    {
                        phoneme = hintPhonemes[i].Trim(),
                        position = i * 120 // 기본 위치 간격
                    });
                }
                
                return new Result { phonemes = phonemes.ToArray() };
            }

            // 확장자 노트 처리 (+, +1, +2 등)
            if (lyric.StartsWith("+"))
            {
                return new Result { phonemes = new Phoneme[0] };
            }

            // 영어 단어 처리 - 공백으로 분리하여 각 부분을 개별 음소로 처리
            if (IsEnglish(lyric))
            {
                var parts = lyric.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    phonemes.Add(new Phoneme { phoneme = part });
                }
            }
            else
            {
                // 한글 분해
                var decomposed = DecomposeHangul(lyric);
                
                for (int i = 0; i < decomposed.Count; i++)
                {
                    var syllable = decomposed[i];
                    string previousVowel = null;
                    
                    // 이전 음절의 모음 정보 가져오기 (VC 조합용)
                    if (i > 0 && !string.IsNullOrEmpty(decomposed[i-1].Medial))
                    {
                        previousVowel = GetVowelFromMedial(decomposed[i-1].Medial);
                    }
                    
                    var syllablePhonemes = ProcessSyllable(syllable, i == 0, previousVowel);
                    phonemes.AddRange(syllablePhonemes);
                }
            }

            // 위치 조정
            AdjustPhonemePositions(phonemes, notes);

            return new Result { phonemes = phonemes.ToArray() };
        }

        /// <summary>
        /// 영어인지 판단
        /// </summary>
        private bool IsEnglish(string text)
        {
            return text.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == ' ' || c == '-');
        }

        /// <summary>
        /// 한글 음절을 자음과 모음으로 분해
        /// </summary>
        private List<HangulSyllable> DecomposeHangul(string text)
        {
            var result = new List<HangulSyllable>();

            foreach (char c in text)
            {
                if (c >= '가' && c <= '힣')
                {
                    // 한글 음절 분해
                    int unicode = c - '가';
                    int initial = unicode / (21 * 28);
                    int medial = (unicode % (21 * 28)) / 28;
                    int final = unicode % 28;

                    var syllable = new HangulSyllable
                    {
                        Initial = GetInitialConsonant(initial),
                        Medial = GetMedialVowel(medial),
                        Final = final > 0 ? GetFinalConsonant(final - 1) : null
                    };

                    result.Add(syllable);
                }
                else
                {
                    // 한글이 아닌 문자는 그대로 처리
                    var syllable = new HangulSyllable
                    {
                        Initial = null,
                        Medial = c.ToString(),
                        Final = null
                    };
                    result.Add(syllable);
                }
            }

            return result;
        }

        /// <summary>
        /// 음절을 음소로 변환
        /// </summary>
        private List<Phoneme> ProcessSyllable(HangulSyllable syllable, bool isFirst = true, string previousVowel = null)
        {
            var phonemes = new List<Phoneme>();

            // 초성이 없거나 ㅇ인 경우 (무음)
            if (string.IsNullOrEmpty(syllable.Initial) || syllable.Initial == "-")
            {
                if (!string.IsNullOrEmpty(syllable.Medial))
                {
                    // 이중모음 대체 처리
                    var vowelPhonemes = ProcessVowelWithFallback(syllable.Medial, isFirst);
                    phonemes.AddRange(vowelPhonemes);
                }
            }
            else
            {
                // 초성이 있는 경우
                phonemes.Add(new Phoneme { phoneme = syllable.Initial });
                if (!string.IsNullOrEmpty(syllable.Medial))
                {
                    var vowelPhonemes = ProcessVowelWithFallback(syllable.Medial, false);
                    phonemes.AddRange(vowelPhonemes);
                }
            }

            // 종성 처리
            if (!string.IsNullOrEmpty(syllable.Final))
            {
                string vowel = previousVowel ?? GetVowelFromMedial(syllable.Medial);
                string finalPhoneme = GetFinalConsonantFromChar(syllable.Final, vowel);
                
                if (!string.IsNullOrEmpty(finalPhoneme))
                {
                    phonemes.Add(new Phoneme { phoneme = finalPhoneme });
                }
            }

            return phonemes;
        }

        /// <summary>
        /// 모음 처리 - 대체 조합 지원
        /// </summary>
        private List<Phoneme> ProcessVowelWithFallback(string vowelStr, bool isFirst)
        {
            var phonemes = new List<Phoneme>();
            if (string.IsNullOrEmpty(vowelStr)) return phonemes;

            char vowel = vowelStr.Replace("- ", "")[0];

            // 대체 조합이 필요한 이중모음인지 확인
            if (diphthongFallbacks.ContainsKey(vowel))
            {
                string diphthongAlias = isFirst ? diphthongs.GetValueOrDefault(vowel) : diphthongsEnding.GetValueOrDefault(vowel);
                
                // 보이스뱅크에 이중모음이 없으면 대체 조합 사용
                if (string.IsNullOrEmpty(diphthongAlias) || 
                    singer?.TryGetMappedOto(diphthongAlias.Replace("- ", ""), 60, "", out _) != true)
                {
                    var (first, second, firstDuration) = diphthongFallbacks[vowel];
                    
                    phonemes.Add(new Phoneme 
                    { 
                        phoneme = isFirst ? $"- {first}" : first,
                        position = 0
                    });
                    
                    phonemes.Add(new Phoneme 
                    { 
                        phoneme = second,
                        position = firstDuration
                    });
                    
                    return phonemes;
                }
            }

            // 기본 모음 처리
            var vowelOnly = vowelStr.Replace("- ", "");
            phonemes.Add(new Phoneme { phoneme = isFirst ? vowelStr : vowelOnly });
            
            return phonemes;
        }

        /// <summary>
        /// 모음에서 실제 모음 문자 추출
        /// </summary>
        private string GetVowelFromMedial(string medial)
        {
            if (string.IsNullOrEmpty(medial)) return null;
            
            // "- a" → "a", "ya" → "ya" 등
            return medial.Replace("- ", "");
        }

        /// <summary>
        /// 받침 문자로부터 직접 받침 음소 생성
        /// </summary>
        private string GetFinalConsonantFromChar(string finalChar, string vowel = null)
        {
            if (string.IsNullOrEmpty(finalChar)) return null;
            
            char final = finalChar[0];
            
            // 기본: 단독 자음
            string defaultConsonant = finalConsonantsOnly.ContainsKey(final) ? finalConsonantsOnly[final] : null;
            
            // VC 조합 시도
            if (!string.IsNullOrEmpty(vowel) && finalConsonants.ContainsKey(final))
            {
                string vcAlias = finalConsonants[final].Replace("a", vowel);
                if (singer?.TryGetMappedOto(vcAlias, 60, "", out _) == true)
                {
                    return vcAlias;
                }
            }
            
            return defaultConsonant;
        }

        /// <summary>
        /// 음소 위치 조정
        /// </summary>
        private void AdjustPhonemePositions(List<Phoneme> phonemes, Note[] notes)
        {
            if (phonemes.Count == 0) return;

            var totalDuration = notes.Sum(n => n.duration);
            var consonantDuration = 60; // 자음 기본 길이

            int currentPosition = 0;
            for (int i = 0; i < phonemes.Count; i++)
            {
                var phoneme = phonemes[i];
                phoneme.position = currentPosition;
                phonemes[i] = phoneme;

                // 마지막 음소(주로 모음)는 남은 길이를 모두 사용
                if (i != phonemes.Count - 1)
                {
                    currentPosition += consonantDuration;
                }
            }
        }

        // 초성 자음 변환
        private string GetInitialConsonant(int index)
        {
            string[] initials = { "ㄱ", "ㄲ", "ㄴ", "ㄷ", "ㄸ", "ㄹ", "ㅁ", "ㅂ", "ㅃ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅉ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };
            if (index < initials.Length && initialConsonants.ContainsKey(initials[index][0]))
            {
                return initialConsonants[initials[index][0]];
            }
            return "";
        }

        // 중성 모음 변환 - 대체 조합 지원
        private string GetMedialVowel(int index)
        {
            string[] medials = { "ㅏ", "ㅐ", "ㅑ", "ㅒ", "ㅓ", "ㅔ", "ㅕ", "ㅖ", "ㅗ", "ㅘ", "ㅙ", "ㅚ", "ㅛ", "ㅜ", "ㅝ", "ㅞ", "ㅟ", "ㅠ", "ㅡ", "ㅢ", "ㅣ" };
            if (index < medials.Length)
            {
                char vowel = medials[index][0];
                
                // 이중모음 먼저 확인
                if (diphthongs.ContainsKey(vowel))
                {
                    return diphthongs[vowel];
                }
                // 단모음 확인
                else if (vowels.ContainsKey(vowel))
                {
                    return vowels[vowel];
                }
            }
            return "";
        }

        /// <summary>
        /// 이중모음 처리 - 없으면 대체 조합 생성
        /// </summary>
        private List<Phoneme> ProcessDiphthong(char vowel, bool isFirst = true)
        {
            var phonemes = new List<Phoneme>();
            
            // 기본 이중모음 시도
            string diphthongAlias = isFirst ? diphthongs.GetValueOrDefault(vowel) : diphthongsEnding.GetValueOrDefault(vowel);
            
            if (!string.IsNullOrEmpty(diphthongAlias))
            {
                // 보이스뱅크에 이중모음이 있는지 확인
                if (singer?.TryGetMappedOto(diphthongAlias.Replace("- ", ""), 60, "", out _) == true)
                {
                    phonemes.Add(new Phoneme { phoneme = diphthongAlias });
                    return phonemes;
                }
            }
            
            // 대체 조합 사용
            if (diphthongFallbacks.ContainsKey(vowel))
            {
                var (first, second, firstDuration) = diphthongFallbacks[vowel];
                
                phonemes.Add(new Phoneme 
                { 
                    phoneme = isFirst ? $"- {first}" : first,
                    position = 0
                });
                
                phonemes.Add(new Phoneme 
                { 
                    phoneme = second,
                    position = firstDuration
                });
            }
            
            return phonemes;
        }

        // 종성 자음 변환 (한글 분해용)
        private string GetFinalConsonant(int index)
        {
            string[] finals = { "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ", "ㄺ", "ㄻ", "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ", "ㅄ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };
            if (index < finals.Length)
            {
                return finals[index];
            }
            return null;
        }
    }

    /// <summary>
    /// 한글 음절 구조체
    /// </summary>
    public class HangulSyllable
    {
        public string Initial { get; set; } // 초성
        public string Medial { get; set; }  // 중성
        public string Final { get; set; }   // 종성
    }
}