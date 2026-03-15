// From pdf.js / web / pdf_find_utils.js

/* Copyright 2018 Mozilla Foundation
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

// import { FeatureTest } from "pdfjs-lib";

const CharacterType = {
  SPACE: 0,
  ALPHA_LETTER: 1,
  PUNCT: 2,
  HAN_LETTER: 3,
  KATAKANA_LETTER: 4,
  HIRAGANA_LETTER: 5,
  HALFWIDTH_KATAKANA_LETTER: 6,
  THAI_LETTER: 7,
};

function isAlphabeticalScript(charCode : any) {
  return charCode < 0x2e80;
}

function isAscii(charCode : any) {
  return (charCode & 0xff80) === 0;
}

function isAsciiAlpha(charCode : any) {
  return (
    (charCode >= /* a = */ 0x61 && charCode <= /* z = */ 0x7a) ||
    (charCode >= /* A = */ 0x41 && charCode <= /* Z = */ 0x5a)
  );
}

function isAsciiDigit(charCode : any) {
  return charCode >= /* 0 = */ 0x30 && charCode <= /* 9 = */ 0x39;
}

function isAsciiSpace(charCode : any) {
  return (
    charCode === /* SPACE = */ 0x20 ||
    charCode === /* TAB = */ 0x09 ||
    charCode === /* CR = */ 0x0d ||
    charCode === /* LF = */ 0x0a
  );
}

function isHan(charCode : any) {
  return (
    (charCode >= 0x3400 && charCode <= 0x9fff) ||
    (charCode >= 0xf900 && charCode <= 0xfaff)
  );
}

function isKatakana(charCode : any) {
  return charCode >= 0x30a0 && charCode <= 0x30ff;
}

function isHiragana(charCode : any) {
  return charCode >= 0x3040 && charCode <= 0x309f;
}

function isHalfwidthKatakana(charCode : any) {
  return charCode >= 0xff60 && charCode <= 0xff9f;
}

function isThai(charCode : any) {
  return (charCode & 0xff80) === 0x0e00;
}

/**
 * This function is based on the word-break detection implemented in:
 * https://hg.mozilla.org/mozilla-central/file/tip/intl/lwbrk/WordBreaker.cpp
 */
function getCharacterType(charCode : any) {
  if (isAlphabeticalScript(charCode)) {
    if (isAscii(charCode)) {
      if (isAsciiSpace(charCode)) {
        return CharacterType.SPACE;
      } else if (
        isAsciiAlpha(charCode) ||
        isAsciiDigit(charCode) ||
        charCode === /* UNDERSCORE = */ 0x5f
      ) {
        return CharacterType.ALPHA_LETTER;
      }
      return CharacterType.PUNCT;
    } else if (isThai(charCode)) {
      return CharacterType.THAI_LETTER;
    } else if (charCode === /* NBSP = */ 0xa0) {
      return CharacterType.SPACE;
    }
    return CharacterType.ALPHA_LETTER;
  }

  if (isHan(charCode)) {
    return CharacterType.HAN_LETTER;
  } else if (isKatakana(charCode)) {
    return CharacterType.KATAKANA_LETTER;
  } else if (isHiragana(charCode)) {
    return CharacterType.HIRAGANA_LETTER;
  } else if (isHalfwidthKatakana(charCode)) {
    return CharacterType.HALFWIDTH_KATAKANA_LETTER;
  }
  return CharacterType.ALPHA_LETTER;
}

let NormalizeWithNFKC : any;
function getNormalizeWithNFKC() {
  NormalizeWithNFKC ||= `\xA0¨ª¯²-µ¸-º¼-¾Ĳ-ĳĿ-ŀŉſǄ-ǌǱ-ǳʰ-ʸ˘-˝ˠ-ˤʹͺ;΄-΅·ϐ-ϖϰ-ϲϴ-ϵϹևٵ-ٸक़-य़ড়-ঢ়য়ਲ਼ਸ਼ਖ਼-ਜ਼ਫ਼ଡ଼-ଢ଼ำຳໜ-ໝ༌གྷཌྷདྷབྷཛྷཀྵჼᴬ-ᴮᴰ-ᴺᴼ-ᵍᵏ-ᵪᵸᶛ-ᶿẚ-ẛάέήίόύώΆ᾽-῁ΈΉ῍-῏ΐΊ῝-῟ΰΎ῭-`ΌΏ´-῾ - ‑‗․-… ″-‴‶-‷‼‾⁇-⁉⁗ ⁰-ⁱ⁴-₎ₐ-ₜ₨℀-℃℅-ℇ℉-ℓℕ-№ℙ-ℝ℠-™ℤΩℨK-ℭℯ-ℱℳ-ℹ℻-⅀ⅅ-ⅉ⅐-ⅿ↉∬-∭∯-∰〈-〉①-⓪⨌⩴-⩶⫝̸ⱼ-ⱽⵯ⺟⻳⼀-⿕　〶〸-〺゛-゜ゟヿㄱ-ㆎ㆒-㆟㈀-㈞㈠-㉇㉐-㉾㊀-㏿ꚜ-ꚝꝰ꟱-ꟴꟸ-ꟹꭜ-ꭟꭩ豈-嗀塚晴凞-羽蘒諸逸-都飯-舘並-龎ﬀ-ﬆﬓ-ﬗיִײַ-זּטּ-לּמּנּ-סּףּ-פּצּ-ﮱﯓ-ﴽﵐ-ﶏﶒ-ﷇﷰ-﷼︐-︙︰-﹄﹇-﹒﹔-﹦﹨-﹫ﹰ-ﹲﹴﹶ-ﻼ！-ﾾￂ-ￇￊ-ￏￒ-ￗￚ-ￜ￠-￦`;
  return NormalizeWithNFKC;
}

export { CharacterType, getCharacterType, getNormalizeWithNFKC };