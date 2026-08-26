# Shape — `S`

What a regiment is, geometrically, and what that shape is used for.

| | Requirement | Priority | Status |
|---|---|---|---|
| S1 | A regiment is a rectangle on the ground with a free bearing, not a token. Every question about nearness asks the rectangle. | Mandatory | ✅ |
| S2 | **Drawn 2:1** — width to depth — whatever the real depth is. A regiment 40 m wide is drawn 20 m deep. | Mandatory | ✅ |
| S3 | The drawn rectangle **is** the collider — the whole of it. What you see is what blocks, what is clicked, and what is hit. | Mandatory | ✅ |
| S4 | The rectangle never shrinks as men die. It is the same size on the last turn of a battle as the first, unless the regiment is wiped out entirely. | Mandatory | ✅ |
| S5 | Real spacing is unchanged by any of this — the men are still a metre apart and ten deep. The rectangle is presentation. | Mandatory | ✅ |
| S5a | **Two shapes, and only two.** The **block** (`Footprint`) is 2:1 and constant, and is what collides, blocks and holds a zone of control. The **real ground** is the true space the men occupy. | Mandatory | ✅ |
| S6 | **Names.** A regiment's real ground is its *space*; the 2:1 rectangle is its *block*. | Mandatory | ⚠️ code still says otherwise in places |
