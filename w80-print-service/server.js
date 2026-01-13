const express = require("express");
const sharp = require("sharp");
const fs = require("fs");
const path = require("path");

const app = express();
app.use(express.json({ limit: "20mb" }));

// ---- ESC/POS helpers ----
const ESC = 0x1B;
const GS = 0x1D;

function escInit() {
    return Buffer.from([ESC, 0x40]); // ESC @
}
function escAlign(align /*0 left, 1 center, 2 right*/) {
    return Buffer.from([ESC, 0x61, align]);
}
function escFeed(n = 3) {
    return Buffer.from([ESC, 0x64, n]); // ESC d n
}
function escCut() {
    return Buffer.from([GS, 0x56, 0x00]); // GS V 0 (full cut) - บางเครื่องเป็น partial แต่ไว้ก่อน
}

/**
 * Convert image to ESC/POS raster (GS v 0)
 * - targetWidth: 576px (80mm @203dpi commonly)
 * - threshold: 0..255 (ยิ่งต่ำยิ่งดำ)
 */
async function imageToEscposRaster(imageBuffer, targetWidth = 576, threshold = 180) {
    const { data, info } = await sharp(imageBuffer)
        .resize({ width: targetWidth, withoutEnlargement: true })
        .grayscale()
        .threshold(threshold)
        .raw()
        .toBuffer({ resolveWithObject: true });

    const width = info.width;
    const height = info.height;

    const widthBytes = Math.ceil(width / 8);
    const xL = widthBytes & 0xff;
    const xH = (widthBytes >> 8) & 0xff;
    const yL = height & 0xff;
    const yH = (height >> 8) & 0xff;

    // GS v 0 m xL xH yL yH
    const header = Buffer.from([GS, 0x76, 0x30, 0x00, xL, xH, yL, yH]);

    const raster = Buffer.alloc(widthBytes * height);

    for (let y = 0; y < height; y++) {
        for (let xByte = 0; xByte < widthBytes; xByte++) {
            let b = 0;
            for (let bit = 0; bit < 8; bit++) {
                const x = xByte * 8 + bit;
                if (x < width) {
                    const px = data[y * width + x]; // threshold แล้ว: 0=black, 255=white
                    if (px === 0) b |= (0x80 >> bit);
                }
            }
            raster[y * widthBytes + xByte] = b;
        }
    }

    return Buffer.concat([header, raster]);
}

// ---- Routes ----
app.get("/health", (_req, res) => res.send("ok"));

/**
 * POST /print/image
 * body: {
 *   imageBase64: string (PNG/JPG),
 *   paperWidth?: 576,
 *   threshold?: 180,
 *   cut?: true,
 *   saveDebug?: true
 * }
 *
 * response: { ok, escposBase64, bytesLength }
 */
app.post("/print/image", async (req, res) => {
    const {
        imageBase64,
        paperWidth = 576,
        threshold = 180,
        cut = true,
        saveDebug = false,
    } = req.body || {};

    if (!imageBase64 || typeof imageBase64 !== "string") {
        return res.status(400).json({ ok: false, error: "imageBase64 is required" });
    }

    try {
        // ✅ รองรับ data URL: data:image/png;base64,xxxx
        const cleaned = imageBase64.includes("base64,")
            ? imageBase64.split("base64,")[1]
            : imageBase64;

        const trimmed = cleaned.trim();
        const imgBuf = Buffer.from(trimmed, "base64");

        if (!imgBuf.length) {
            return res.status(400).json({
                ok: false,
                error: "imageBase64 decoded to empty buffer (check base64 content)",
            });
        }

        const raster = await imageToEscposRaster(imgBuf, paperWidth, threshold);

        const payload = Buffer.concat([
            escInit(),
            escAlign(1),
            raster,
            escFeed(3),
            cut ? escCut() : Buffer.alloc(0),
            escAlign(0),
        ]);

        if (saveDebug) {
            const outDir = path.join(process.cwd(), "out");
            if (!fs.existsSync(outDir)) fs.mkdirSync(outDir);
            const outFile = path.join(outDir, `job_${Date.now()}.bin`);
            fs.writeFileSync(outFile, payload);
        }

        return res.json({
            ok: true,
            bytesLength: payload.length,
            escposBase64: payload.toString("base64"),
        });
    } catch (e) {
        return res.status(500).json({ ok: false, error: String(e) });
    }
});


const PORT = process.env.PORT || 9109;
app.listen(PORT, "127.0.0.1", () => {
    console.log(`w80-print-service (dev) running on http://127.0.0.1:${PORT}`);
});
