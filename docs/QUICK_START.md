# Quick start

AstroProject Forge prepares an organized PixInsight WeightedBatchPreprocessing project without modifying original files.

1. Add FITS/XISF folders or files under **Sources**.
2. Add one or more Master Dark/Bias folders under **Calibration libraries**.
3. Select **Analyze**.
4. Open **Issues** and resolve only the reported ambiguities.
5. Open **WBPP**, copy only the listed Grouping Keywords and set `Pre = ON`, `Post = OFF`.
6. Optionally inspect each filter/configuration session under **Quality**.
7. Open **Export**, choose a destination and export the verified structure.

An observing night may cross midnight. The configurable night boundary keeps post-midnight frames with the preceding evening.

For help use **Menu → Quick guide**. To report an error use **Menu → Report a problem** and attach the support ZIP from **Menu → Diagnostics** when possible.
