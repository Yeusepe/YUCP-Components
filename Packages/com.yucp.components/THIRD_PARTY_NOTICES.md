# Third-Party Notices

## SPIRE EMA Corpus-derived articulatory coefficients

`Runtime/Components/Data/Generated/AdvancedVisemeTransitionRetention.generated.cs`
and `Runtime/Components/Data/Generated/AdvancedVisemeVisibleTongueResidual.generated.cs`,
plus `Runtime/Components/Data/Generated/AdvancedVisemeHiddenPhonePosterior.generated.cs`,
and `Runtime/Components/Data/Generated/AdvancedVisemeOculusHalo.generated.cs`,
and `Runtime/Components/Data/Generated/AdvancedVisemeOculusDynamics.generated.cs`,
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
for the speech-only shape model, normalized continuous Oculus frames were
aggregated by their dominant winner, reduced with an exact sparse-simplex
projection into a five-support base, and fitted by elapsed-winner time to five
simplex control points. They form a 168 ms cubic and a 56 ms linear tail.
The total target trajectory is 224 ms;
values were aggregated across documented train/development subsets and evaluated
on held-out speakers and sentences. No raw trajectories, audio, speaker geometry,
absolute corpus coordinates, or Oculus binary are redistributed.

The generated coefficients remain subject to CC BY 4.0. The surrounding YUCP
source code remains under the package license.

## Advanced Viseme trajectory-method references

The duration-conditioned target design was informed by research that models visual
speech as a trajectory rather than a sequence of unrelated static poses:

- M. Tamura, T. Masuko, T. Kobayashi, and K. Tokuda, “Visual Speech Synthesis Based on Parameter Generation From HMM: Speech-Driven and Text-And-Speech-Driven Approaches,” AVSP 1998. [ISCA Archive](https://www.isca-archive.org/avsp_1998/tamura98_avsp.html)
- K. Tokuda, H. Zen, and T. Kitamura, “Trajectory modeling based on HMMs with the explicit relationship between static and dynamic features,” Eurospeech 2003. [DOI 10.21437/Eurospeech.2003-195](https://doi.org/10.21437/Eurospeech.2003-195)
- H. Zen, K. Tokuda, T. Masuko, T. Kobayashi, and T. Kitamura, “Hidden semi-Markov model based speech synthesis,” Interspeech 2004. [ISCA Archive](https://www.isca-archive.org/interspeech_2004/zen04b_interspeech.html)
- T. Toda and K. Tokuda, “Speech parameter generation algorithm considering global variance for HMM-based speech synthesis,” Interspeech 2005. [ISCA Archive](https://www.isca-archive.org/interspeech_2005/toda05b_interspeech.html)
- L. Bao et al., “Learning Audio-Driven Viseme Dynamics for 3D Face Animation,” 2023. [arXiv:2301.06059](https://arxiv.org/abs/2301.06059)

No paper code, pretrained model, or paper coefficient is included. YUCP does not
run an HMM, neural network, audio model, or future-phone lookahead on the avatar;
it fits its own constrained cubic coefficients offline and evaluates only the
current hard winner and its elapsed state time at runtime.
