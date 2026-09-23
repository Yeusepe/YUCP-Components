# Third-Party Notices

## SPIRE EMA Corpus-derived articulatory coefficients

`Runtime/Components/Data/Generated/AdvancedVisemeTransitionRetention.generated.cs`
and `Runtime/Components/Data/Generated/AdvancedVisemeVisibleTongueResidual.generated.cs`,
plus `Runtime/Components/Data/Generated/AdvancedVisemeHiddenPhonePosterior.generated.cs`,
contain dimensionless aggregate coefficients adapted from the **SPIRE EMA
Corpus** by J. Bandekar, S. Udupa, and P. K. Ghosh.

- Pinned source: [SpireLab/SPIRE_EMA_CORPUS revision 55f21628](https://huggingface.co/datasets/SpireLab/SPIRE_EMA_CORPUS/tree/55f21628de95514e3ff22eaccc75e1547d181297)
- Paper: J. Bandekar, S. Udupa, and P. K. Ghosh, “Articulatory synthesis using representations learnt through phonetic label-aware contrastive loss,” Interspeech 2024, pp. 427–431. [DOI 10.21437/Interspeech.2024-1756](https://doi.org/10.21437/Interspeech.2024-1756)
- Source license: [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)

Changes made by YUCP: forced-aligned ARPAbet labels were deterministically
mapped to the 15 Oculus/VRChat viseme classes; normalized EMA trajectories were
reduced to bounded transition-retention coefficients for jaw, lips, tongue tip,
and tongue body, plus bounded visible-face-to-tongue-tip residual coefficients.
For the hidden-phone posterior, paired corpus audio was processed by the locally
installed Oculus LipSync 1.54 Enhanced provider and its dominant viseme history
was combined with calibrated EMA face dynamics to derive aggregate `/m/` versus
`/n,l/` compatibility coefficients;
values were aggregated across documented train/development subsets and evaluated
on held-out speakers and sentences. No raw trajectories, audio, speaker geometry,
absolute corpus coordinates, or Oculus binary are redistributed.

The generated coefficients remain subject to CC BY 4.0. The surrounding YUCP
source code remains under the package license.
