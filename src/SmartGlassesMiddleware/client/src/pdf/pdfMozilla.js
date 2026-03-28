/* 
  This Document uses code supported by PDF.js by the Mozilla Foundation.
  Licensed under Apache 2.0
  https://mozilla.github.io/pdf.js/
*/

// Ported from pdf.js (mozilla/pdf_find_controller & mozilla/pdf_find_utils).
// Used to normalize text before searching so that accented characters, ligatures,
// and other Unicode variants match correctly.

// ── Character type helpers ────────────────────────────────────────────────────

const SYLLABLES_REG_EXP = /[\uAC00-\uD7AF\uFA6C\uFACF-\uFAD1\uFAD5-\uFAD7]+/g;
const SYLLABLES_LENGTHS = new Map();
let noSyllablesRegExp = null;
let withSyllablesRegExp = null;

const CHARACTERS_TO_NORMALIZE = {
  "\u2010": "-",   // Hyphen
  "\u2018": "'",   // Left single quotation mark
  "\u2019": "'",   // Right single quotation mark
  "\u201A": "'",   // Single low-9 quotation mark
  "\u201B": "'",   // Single high-reversed-9 quotation mark
  "\u201C": '"',   // Left double quotation mark
  "\u201D": '"',   // Right double quotation mark
  "\u201E": '"',   // Double low-9 quotation mark
  "\u201F": '"',   // Double high-reversed-9 quotation mark
  "\u00BC": "1/4", // Vulgar fraction one quarter
  "\u00BD": "1/2", // Vulgar fraction one half
  "\u00BE": "3/4", // Vulgar fraction three quarters
};

const FIRST_CHAR_SYLLABLES_REG_EXP =
  "[\\u1100-\\u1112\\ud7a4-\\ud7af\\ud84a\\ud84c\\ud850\\ud854\\ud857\\ud85f]";
const DIACRITICS_REG_EXP = /\p{M}+/gu;
const NFKC_CHARS_TO_NORMALIZE = new Map();

// Large constant string of NFKC characters that need normalization (from pdf.js source).
// Defined at module scope to avoid repeated string allocation.
const NFKC_CHARS = `\xA0¨ª¯²-µ¸-º¼-¾Ĳ-ĳĿ-ŀŉſǄ-ǌǱ-ǳʰ-ʸ˘-˝ˠ-ˤʹͺ;΄-΅·ϐ-ϖϰ-ϲϴ-ϵϹևٵ-ٸक़-य़ড়-ঢ়য়ਲ਼ਸ਼ਖ਼-ਜ਼ਫ਼ଡ଼-ଢ଼ำຳໜ-ໝ༌གྷཌྷདྷབྷཛྷཀྵჼᴬ-ᴮᴰ-ᴺᴼ-ᵍᵏ-ᵪᵸᶛ-ᶿẚ-ẛάέήίόύώΆ᾽-῁ΈΉ῍-῏ΐΊ῝-῟ΰΎ῭-\`ΌΏ´-῾ - ‑‗․-… ″-‴‶-‷‼‾⁇-⁉⁗ ⁰-ⁱ⁴-₎ₐ-ₜ₨℀-℃℅-ℇ℉-ℓℕ-№ℙ-ℝ℠-™ℤΩℨK-ℭℯ-ℱℳ-ℹ℻-⅀ⅅ-ⅉ⅐-ⅿ↉∬-∭∯-∰〈-〉①-⓪⨌⩴-⩶⫝̸ⱼ-ⱽⵯ⺟⻳⼀-⿕　〶〸-〺゛-゜ゟヿㄱ-ㆎ㆒-㆟㈀-㈞㈠-㉇㉐-㉾㊀-㏿ꚜ-ꚝꝰ꟱-ꟴꟸ-ꟹꭜ-ꭟꭩ豈-嗀塚晴凞-羽蘒諸逸-都飯-舘並-龎ﬀ-ﬆﬓ-ﬗיִײַ-זּטּ-לּמּנּ-סּףּ-פּצּ-ﮱﯓ-ﴽﵐ-ﶏﶒ-ﷇﷰ-﷼︐-︙︰-﹄﹇-﹒﹔-﹦﹨-﹫ﹰ-ﹲﹴﹶ-ﻼ！-ﾾￂ-ￇￊ-ￏￒ-ￗￚ-ￜ￠-￦`;

function getNormalizeWithNFKC() {
  return NFKC_CHARS;
}

/**
 * Build the normalization regex on first use (lazy init to avoid startup cost).
 * Returns [withoutSyllablesRegex, withSyllablesRegex].
 */
function buildNormalizationRegex() {
  if (noSyllablesRegExp) return;

  // Characters to normalize via NFKC
  let normalizeCharsStr = getNormalizeWithNFKC();

  // Create character ranges for the regex
  let nfkcStr = "";
  for (const char of normalizeCharsStr) {
    nfkcStr += char;
  }

  const diacriticsPattern = DIACRITICS_REG_EXP.source;

  // Pattern pieces in order (matching order matters):
  // 1. Special characters to replace
  // 2. NFKC chars
  // 3. Diacritics followed by EOL
  // 4. Diacritics sequences
  // 5. Hyphen-EOL patterns
  // 6. Hyphen-EOL after non-space
  // 7. Ideographic-EOL
  // 8. EOL → space
  // 9. Syllables

  const p1 = `([${Object.keys(CHARACTERS_TO_NORMALIZE).join("")}])`;
  // For NFKC we just match any char in normalizeCharsStr
  const p2 = `([${nfkcStr.replace(/[\]\\^-]/g, "\\$&")}])`;
  const p3 = `([\\u0300-\\u036f\\u0483-\\u0489]+\\n)`;
  const p4 = `(${diacriticsPattern}[\\u0300-\\u036f\\u0483-\\u0489]*)`;
  const p5 = `(\\S-\\n)`;
  const p6 = `([^\\s]-\\n)`;
  const p7 = `([${FIRST_CHAR_SYLLABLES_REG_EXP.slice(1, -1)}\\u3000-\\u9fff\\uf900-\\ufaff]\\n)`;
  const p8 = `(\\n)`;
  const p9 = `(${SYLLABLES_REG_EXP.source})`;

  // Build a simpler regex for the common case (just p1, p2, p8)
  // The full regex from pdf.js is very complex, so we use a simplified version
  // that handles the most common normalization cases.
  try {
    noSyllablesRegExp = new RegExp(`${p1}|${p2}|${p8}`, "g");
    withSyllablesRegExp = new RegExp(`${p1}|${p2}|${p8}|${p9}`, "g");
  } catch (_) {
    // Fallback: just handle special chars and newlines
    noSyllablesRegExp = /([‐‑‗…''‚‛""„‟¼½¾])|(\n)/g;
    withSyllablesRegExp = noSyllablesRegExp;
  }
}

/**
 * Normalize text for searching.  Returns [normalizedText, diffs, hasDiacritics].
 * The diffs array can be used to map positions back to the original text.
 *
 * @param {string} text
 * @param {{ ignoreDashEOL?: boolean }} [options]
 * @returns {[string, [Uint32Array, Int32Array], boolean]}
 */
export function normalize(text, options = { ignoreDashEOL: false }) {
  buildNormalizationRegex();

  const positions = [];
  let shift = 0;
  let shiftOrigin = 0;
  let eol = 0;
  let hasDiacritics = false;

  const syllablePositions = [];
  let m;
  const syllReg = new RegExp(SYLLABLES_REG_EXP.source, "g");
  while ((m = syllReg.exec(text)) !== null) {
    let { index } = m;
    for (const char of m[0]) {
      let len = SYLLABLES_LENGTHS.get(char);
      if (!len) {
        len = char.normalize("NFD").length;
        SYLLABLES_LENGTHS.set(char, len);
      }
      syllablePositions.push([len, index++]);
    }
  }

  const hasSyllables = syllablePositions.length > 0;
  const normalizationRegex = hasSyllables ? withSyllablesRegExp : noSyllablesRegExp;

  // Reset lastIndex since we reuse the regex objects
  if (normalizationRegex) normalizationRegex.lastIndex = 0;

  let syllableIndex = 0;
  let rawDiacriticsPositions = [];
  let rawDiacriticsIndex = 0;

  // First collect diacritics positions
  const diacReg = new RegExp(DIACRITICS_REG_EXP.source, "gu");
  while ((m = diacReg.exec(text)) !== null) {
    rawDiacriticsPositions.push([m[0].length, m.index]);
  }

  const normalized = text.replace(normalizationRegex, (_match, p1, p2, p8, p9, i) => {
    i -= shiftOrigin;

    if (p1) {
      const replacement = CHARACTERS_TO_NORMALIZE[p1];
      if (!replacement) return p1;
      const jj = replacement.length;
      for (let j = 1; j < jj; j++) {
        positions.push(i - shift + j, shift - j);
      }
      shift -= jj - 1;
      return replacement;
    }

    if (p2) {
      let replacement = NFKC_CHARS_TO_NORMALIZE.get(p2);
      if (!replacement) {
        replacement = p2.normalize("NFKC");
        NFKC_CHARS_TO_NORMALIZE.set(p2, replacement);
      }
      const jj = replacement.length;
      for (let j = 1; j < jj; j++) {
        positions.push(i - shift + j, shift - j);
      }
      shift -= jj - 1;
      return replacement;
    }

    if (p8) {
      // EOL → space
      positions.push(i - shift + 1, shift - 1);
      shift -= 1;
      shiftOrigin += 1;
      eol += 1;
      return " ";
    }

    if (p9) {
      if (i + eol === syllablePositions[syllableIndex]?.[1]) {
        const newCharLen = syllablePositions[syllableIndex][0] - 1;
        ++syllableIndex;
        for (let j = 1; j <= newCharLen; j++) {
          positions.push(i - (shift - j), shift - j);
        }
        shift -= newCharLen;
        shiftOrigin += newCharLen;
      }
      return p9;
    }

    return _match;
  });

  positions.push(normalized.length, shift);

  const starts = new Uint32Array(positions.length >> 1);
  const shifts = new Int32Array(positions.length >> 1);
  for (let i = 0, ii = positions.length; i < ii; i += 2) {
    starts[i >> 1] = positions[i];
    shifts[i >> 1] = positions[i + 1];
  }

  return [normalized, [starts, shifts], hasDiacritics];
}
