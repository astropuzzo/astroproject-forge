# Quick start

AstroProject Forge prepares an organized PixInsight WeightedBatchPreprocessing project without modifying original files.

**First time?** Choose **Try the demo project** in the welcome wizard (or **Menu → Open the demo project**). Forge builds two synthetic nights of the Cygnus Loop (ASIAIR and N.I.N.A.) with flats and a master library, analyses them and walks you through every screen on the real controls. Press **F1** on any screen to replay its part of the tour, **Shift+F1** for the online guide.

The window has four steps, always in order, and one button at the bottom right that does the next thing the project needs.

1. **Import.** Link your Master Dark/Bias folder under **Master Library** (once: it is remembered for every project), add FITS/XISF folders or files under **Captures**, then select **Analyze**. The analysis carries on to step 2 by itself. You can also drag folders and files from your file manager onto the window, on any step: the left half is the Master Library, the right half the captures.
2. **Gear.** Check the camera, optics, reducer, pixel size and filters Forge read from the headers (a project that mixes rigs offers one card per rig; pick the one to check). Anything that is not right can be changed: select **Change**, or tap a part of the optical train. Filters can be changed too, even the ones Forge recognised. What you choose is remembered for that camera. The **Field of view** shows the real sky around your target (a DSS2 photo from the CDS in Strasbourg) with the sensor frame at its true place, to scale and turned as the camera was (position from the solved WCS or the target in the Lights, angle from the WCS or the rotator), and the Moon at the same scale to show how big the field is. Only the field's position and size are sent to download it, never files or project data; a photo already seen opens offline, and the **Menu** can turn it off. With no coordinates or no network the frame stays in place on an empty sky and says why.
3. **Calibration.** See how many Lights have Flat, Dark and Bias. When Forge cannot choose by itself it asks one thing at a time.
4. **Export.** Name the project, choose a destination and export the verified structure. Then select **Open in PixInsight**: the generated `.xpsm` already contains the files, masters, grouping keywords and output directory.

Statistics, frame quality and metadata are tools under **Tools** (top right), not steps.

An observing night may cross midnight. The configurable night boundary keeps post-midnight frames with the preceding evening.

For help use **Menu → Quick guide**. To report an error use **Menu → Report a problem** and attach the support ZIP from **Menu → Diagnostics** when possible.
