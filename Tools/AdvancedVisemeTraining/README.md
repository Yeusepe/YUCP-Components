# Advanced Viseme Transition Training

This tool derives a compact, avatar-portable coarticulation table from real electromagnetic articulography (EMA). It does **not** put corpus coordinates into an avatar. Instead, it learns the dimensionless amount of the avatar's own previous authored pose that should remain at the beginning of each hard-viseme transition.

The generated model contains four independent `15 x 15` tables:

- `Jaw`
- `Lips` (upper and lower lip sensors together)
- `TongueTip`
- `TongueBody` (tongue body and dorsum sensors together)

This matters for custom avatars: the learned value blends that avatar's authored source and destination poses, so it is compatible with custom blendshapes, tailored VRCFaceTracking templates, and nonstandard facial proportions.

## Source and license

The coefficients are derived from the official [SpireLab/SPIRE_EMA_CORPUS](https://huggingface.co/datasets/SpireLab/SPIRE_EMA_CORPUS) release at pinned revision `55f21628de95514e3ff22eaccc75e1547d181297`. Its dataset card declares [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The pinned `processed.zip` object is 3,086,792,402 bytes with SHA-256/LFS object ID `ea1c3440af2b69cef0765b97e1e533ea72dae6029c49a175e1a93761c4236d04`.

Required attribution:

> Bandekar, J., Udupa, S., and Ghosh, P. K. (2024). Articulatory synthesis using representations learnt through phonetic label-aware contrastive loss. Proc. Interspeech 2024, 427–431. [DOI 10.21437/Interspeech.2024-1756](https://www.isca-archive.org/interspeech_2024/bandekar24_interspeech.html).

The paper documents 38 fluent English speakers, 460 MOCHA-TIMIT sentences, and six midsagittal EMA sensors: upper lip, lower lip, jaw, tongue tip, tongue body, and tongue dorsum. The runtime generated file repeats the CC BY attribution. No raw EMA trajectories are committed to this repository.

The published processed tensors run at approximately 100 Hz, verified by matching their frame counts to the paired WAV durations. The paper's 62.5 Hz value describes a later synthesis/vocoder downsampling step; it is not used as the source tensor rate. Generated timing is therefore emitted in seconds (`0.05` or `0.06` seconds), not ambiguous corpus frames.

## Reproduce

From the repository root in PowerShell:

```powershell
python -m venv "$env:TEMP\yucp-avt-venv"
& "$env:TEMP\yucp-avt-venv\Scripts\python.exe" -m pip install -r Tools\AdvancedVisemeTraining\requirements.txt
& "$env:TEMP\yucp-avt-venv\Scripts\python.exe" Tools\AdvancedVisemeTraining\train_transition_retention.py all --cache-dir "$env:LOCALAPPDATA\YUCP\AdvancedVisemeTraining\SPIRE_EMA_CORPUS" --workers 4
```

`fetch` and `train` can also be run separately. Fetching is resumable. The tool range-reads 544 selected members (about 90.5 MiB compressed) rather than downloading the 3.09 GB archive. Every uncompressed member is bound by size, ZIP CRC-32, and SHA-256; a canonical selection SHA-256 binds the ordered entries to the pinned repository revision and LFS object ID. Cache paths are resolved and required to stay under the selected cache directory before any read or write.

Because range fetching does not read all 3.09 GB, it cannot independently recompute the complete LFS object digest. The pinned revision/object ID plus the per-entry SHA-256 values precisely bind the subset that is actually used.

The `.pt` members are ZIP containers with a NumPy pickle payload. The loader does not call `torch.load` or unrestricted `pickle.load`; it permits only NumPy array/dtype reconstruction and `_codecs.encode`, and rejects tensor-storage members or any unexpected global.

The source schema is checked against pinned repository/revision/archive/sample-rate/viseme/group constants. Practical caps are enforced before allocation or extraction: JSON size, archive and selection member counts, selected bytes, `.pt` size, inner ZIP members and expansion, pickle bytes, EMA frames/dtype/dimensions, phone count, label length, and duration range. Updating the source intentionally therefore requires changing both the manifest and the reviewed constants in the trainer.

Generated outputs:

- `Tools/AdvancedVisemeTraining/Generated/advanced_viseme_transition_retention.json` — full provenance, counts, backoff levels, tuning grid, and evaluation.
- `Tools/AdvancedVisemeTraining/Generated/spire_selection_manifest.json` — the canonical 544-entry list with per-member size, CRC-32, SHA-256, split, speaker, and prompt identity.
- `Packages/com.yucp.components/Runtime/Components/Data/Generated/AdvancedVisemeTransitionRetention.generated.cs` — compact runtime table and accessors.

## Visible-face to tongue-tip residual

`train_visible_tongue_residual.py` adds two separately fitted, Beta-only models for the case where visible lower-face tracking is available but native tongue tracking is not:

| Model | Visible inputs | Runtime outputs |
|---|---|---|
| Balanced | Jaw open, lip aperture, lip protrusion | `TongueOut`, `TongueY` |
| Quality | Balanced inputs plus jaw advance | `TongueOut`, `TongueY` |

Balanced is a true three-input fit. It is not the Quality model with jaw advance set to zero. Both fits use the head-corrected `ema_trimmed` coordinates in their shared physical coordinate frame. The geometric semantics are formed before per-speaker calibration:

```text
JawOpen       = UpperLipY - JawY
JawAdvance    = JawX - UpperLipX
LipAperture   = UpperLipY - LowerLipY
LipProtrusion = mean(UpperLipX, LowerLipX)
TongueOut     = TongueTipX - mean(UpperLipX, LowerLipX)
TongueY       = TongueTipY - JawY
```

The published 12-D `ema_trimmed_and_normalised_with_6_articulators` tensor uses articulator-specific standardization. Differences or averages across its channels are therefore not physical geometry and are intentionally not used. SPIRE's raw jaw X axis is anterior/posterior movement; at runtime that semantic measurement maps to Unified Expressions `JawZ`/`JawForward`, never lateral `JawX`.

Each runtime semantic channel is calibrated to `[0,1]`, centered on the reconstructed viseme pose, and divided by its sign-dependent available headroom. Exactly one 24 ms two-pole observer starts from those unfiltered calibrated/headroom-normalized values. Training initializes both poles from the first sample. The avatar graph starts conservatively instead: its tracking-confidence and out-of-distribution ramps abstain while the poles settle. Passing an already smoothed tracking value into this observer changes the trained transfer function. It produces three causal feature stages per channel:

```text
current, current - fast, fast - slow
```

The low-rank model is evaluated as:

```text
h = x * inputProjection
t = sum_v p[v] * (visemeBias[v] + h * visemeMix[v])
r = clamp((t * outputProjection) * sum_v p[v] * reliability[v], -1, 1)
```

`r` is a dimensionless residual fraction of the avatar's remaining authored channel range. It must be applied by headroom interpolation, never by unbounded addition, and measured `TongueOut`/`TongueY` tracking must take precedence. Both models are fitted directly to tongue-tip advance and height; the midsagittal corpus cannot justify tongue lateral motion, roll, twist, or asymmetry.

The generated class also emits the exactly collapsed affine coefficients used by `PredictUnclamped`, empirical training `|feature|` p99/p99.5 values for out-of-distribution diagnostics, and conservative algebraic envelopes. `FeatureSafeBound` is `1` for the current stage and `2` for both difference stages. The latent/conditional/output envelopes are chained from the previously emitted, outward-rounded float32 envelope with a deterministic `1.0001` inflation. Empirical quantiles are diagnostics, not hard clamps or evidence that a VRCFT stream follows the EMA distribution.

To reproduce both checked models from the already fetched, hash-validated cache:

```powershell
& "$env:TEMP\yucp-avt-venv\Scripts\python.exe" Tools\AdvancedVisemeTraining\train_visible_tongue_residual.py train --cache-dir "$env:LOCALAPPDATA\YUCP\AdvancedVisemeTraining\SPIRE_EMA_CORPUS"
```

The trainer reuses the restricted NumPy loader, pinned source manifest, canonical 544-entry selection, and speaker/sentence splits from `train_transition_retention.py`. It fails unless retraining reproduces the reviewed Quality source-model hash `d8a567ea3b660c88f6ff451fea731a992d8ae97226c1927a875667bfec7f9279` and Balanced source-model hash `c1d330639540c2d65c359762656fd5a9f6d669f7100f0437c43b3f3b02d2c93c`. The enriched committed audits are independently pinned as Quality `eed78685582dbc0949c0102b1137eada80ab8c786357471481cab22ed15069f9` and Balanced `0bcc06e661ed1b7e4e45a03a6051a3fcb51c65077d3326a4c85948f2cad8f5d8`; a merely self-consistent edited JSON file is rejected. To validate those audits and deterministically regenerate only C#:

```powershell
& "$env:TEMP\yucp-avt-venv\Scripts\python.exe" Tools\AdvancedVisemeTraining\train_visible_tongue_residual.py generate
```

Additional outputs:

- `Tools/AdvancedVisemeTraining/Generated/advanced_viseme_visible_tongue_residual.json` — Quality projection, provenance, metrics, and coefficients.
- `Tools/AdvancedVisemeTraining/Generated/advanced_viseme_visible_tongue_residual_balanced.json` — independent Balanced model and conservative sensitivity notes.
- `Packages/com.yucp.components/Runtime/Components/Data/Generated/AdvancedVisemeVisibleTongueResidual.generated.cs` — both runtime tables behind one API.

The final fit uses speakers 1–8; speaker-LOSO evaluation is performed across those eight speakers. Speakers 9–12 and their disjoint prompt range remain held out. Within each evaluation speaker, every fourth prompt is used only to simulate tracker range calibration and the other three quarters are scored, leaving 25,329 held-out evaluation frames.

| Model | Speaker-LOSO | Held-out overall | `TongueOut` held-out | `TongueY` held-out |
|---|---:|---:|---:|---:|
| Balanced | +10.328% | +11.663% | +9.914% | +13.340% |
| Quality | +12.491% | +14.876% | +14.594% | +15.147% |

A deterministic 100,000-resample sensitivity analysis (seed `20260713`) remained positive for both outputs. Stratified-utterance 95% intervals were Balanced `TongueOut` `[+5.730%, +9.904%, +13.850%]`, Balanced `TongueY` `[+10.685%, +13.346%, +15.895%]`, Quality `TongueOut` `[+10.567%, +14.602%, +18.321%]`, and Quality `TongueY` `[+12.546%, +15.159%, +17.636%]`. Whole-speaker cluster intervals were also positive, but there are only four held-out speakers; the exact per-speaker points and intervals are retained in each audit.

These percentages measure residual prediction inside the held-out SPIRE EMA domain. The evaluation has 25,329 held-out EMA frames but **zero paired Unified Expressions/VRCFT frames**, so live-tracker domain-transfer error and calibration bias currently have no numerical bound. The scores therefore do **not** measure accuracy on a live tracker and cannot establish equivalence to native tongue tracking. In particular, runtime lip aperture (`MouthOpen` opposed by `MouthClosed`) and protrusion (`LipFunnel`/`LipPucker` opposed by `LipSuck`) are semantic proxies for the EMA geometry. Until paired UE+EMA or UE+native-tongue captures are evaluated, the estimator must remain a bounded, confidence-faded Beta fallback that yields to measured tongue channels.

## Face-conditioned hidden-phone posterior

`train_hidden_phone_posterior.py` trains a separate Beta-only compatibility posterior for one defensible ambiguity: `/m/` versus `/n,l/`. It does not relabel VRChat's public viseme output. It only redistributes the combined `PP` and `nn` probability inside the hidden tongue-tip and tongue-body priors, while measured jaw/lip/tongue channels retain their normal fusion precedence.

The trainer uses the paired SPIRE audio and EMA from the same pinned 544 utterances. Audio is resampled deterministically from 16 kHz PCM16 to 48 kHz and processed causally in 1024-sample buffers by the package's installed `OVRLipSync.dll`: version 1.54.0, Enhanced provider, smoothing 70, SHA-256 `2318c42eb806753e340b426d0b83dd3278f5b8d3b851ccf90a5be0ea8e1d2cd3`. Each 100 Hz EMA frame sees only the newest Oculus result already available at that time. The runtime surrogate then reproduces Beta's exact 24 ms viseme observer, group-specific retention centers, and common-fast observation mixture.

Three independently selected feature tiers are emitted:

| Model | Face axes | Causal features |
|---|---|---:|
| Aperture | jaw opening, lip aperture | 6 |
| Balanced | Aperture plus lip protrusion | 9 |
| Quality | Balanced plus jaw advance | 12 |

Each axis contributes `current`, `current - fast`, and `fast - slow`. A shared visible-face log-likelihood is combined with a Dirichlet-smoothed Oculus-winner prior. `/p/` and `/b/` closures are not counted as `/n,l/` negatives in the M-versus-N/L discriminant. A separate Beta(1,1)-smoothed eligibility table estimates `P(M or N/L eligible | hard Oculus winner)` from **every** forced-phone occurrence; stops, vowels, silence, and unrelated consonants all remain in its ineligible denominator. At runtime, that eligibility, posterior margin, speech presence, tracker activity, observer-response compatibility, and an empirical feature-support envelope gate the correction continuously. Current features are bounded to 1 and observer differences to 2 by construction.

To reproduce the checked audit and generated C# from the validated cache and installed Oculus plugin:

```powershell
& "$env:TEMP\yucp-avt-venv\Scripts\python.exe" Tools\AdvancedVisemeTraining\train_hidden_phone_posterior.py all --cache-dir "$env:LOCALAPPDATA\YUCP\AdvancedVisemeTraining\SPIRE_EMA_CORPUS"
```

Generated outputs:

- `Tools/AdvancedVisemeTraining/Generated/spire_audio_selection_manifest.json` — pinned paired-audio selection and per-member hashes.
- `Tools/AdvancedVisemeTraining/Generated/advanced_viseme_hidden_phone_posterior.json` — complete provenance, parity specification, coefficients, support bounds, controls, and evaluation.
- `Packages/com.yucp.components/Runtime/Components/Data/Generated/AdvancedVisemeHiddenPhonePosterior.generated.cs` — compact Aperture/Balanced/Quality runtime API.

The generated content SHA-256 is `718707ccdc47a9d3324d2141f56ffc212f77a67e757ae5895b61d2b704dad1b6`. Ridge strength and model choice were selected on development data only. Held-out speakers 9–12 and disjoint sentences produced:

| Model | NLL | Brier | ECE | Accuracy | F1 `/m/` | True `/m/` emitted as `nn`, recovered |
|---|---:|---:|---:|---:|---:|---:|
| Aperture | 0.20201 | 0.05703 | 0.03015 | 0.92402 | 0.92422 | 40 / 50 |
| Balanced | 0.20239 | 0.05692 | 0.03211 | 0.92275 | 0.92295 | 40 / 50 |
| Quality | 0.21284 | 0.06045 | 0.02952 | 0.91679 | 0.91681 | 39 / 50 |

Quality won the development split and remains the selected rich model even though Aperture and Balanced were slightly better on held-out data; the held-out set was not used to retune that choice. The hard-Oculus-observation baseline Brier score was reduced by about 74.5% for Aperture/Balanced and 72.9% for Quality. These are SPIRE EMA/Oculus surrogate results, not measured live-VRCFT accuracy: EMA and Unified Expressions are unpaired semantic domains.

The held-out all-phone eligibility gate reached NLL 0.376876 versus 0.396702 for the training-global-prior baseline, Brier 0.112704 versus 0.117119, and ECE 0.021487; empirical eligibility was 0.135437 and mean predicted eligibility was 0.134367. This lowers the mixed reliability of `PP` to 0.207913 and `nn` to 0.305022. It is intentionally conservative: an unrelated phone with a decaying `PP/nn` observer tail cannot receive full hidden-phone authority.

A closed mouth supplies evidence for bilabial place, not proof of `/m/`; `/p/` and `/b/` can look the same. Likewise, visible lower-face motion cannot safely recover voicing pairs (`p/b`, `t/d`, `k/g`, `s/z`) or guarantee `n/l`. The model therefore exposes a confidence-gated compatibility posterior and must abstain smoothly, never present inferred tongue motion as native tongue tracking.

## Deterministic subset and evaluation

Prompt IDs use the full-period affine permutation

```text
prompt(ordinal) = ((137 * ordinal + 41) mod 460) + 1
```

The checked-in model uses:

- fit: speakers 1–6, prompt ordinals 0–63;
- development: speakers 7–8, disjoint prompt ordinals 96–111;
- held-out evaluation: speakers 9–12, disjoint prompt ordinals 64–95.

All three splits are speaker- and sentence-disjoint. Decay and regularization are selected only on the development split. The final coefficients use fit plus development; the reported test has both unseen speakers and unseen sentences.

For group `g`, previous viseme `a`, current viseme `b`, and frame offset `k`, the fitted predictor is

```text
y_hat(t) = mu_b + r[g,a,b] * exp(-t_seconds / decay_seconds[g]) * (mu_a - mu_b)
```

`r` is the clamped least-squares carryover in `[0,1]`. Direct cells are shrunk toward a destination-viseme prior; sparse cells back off to destination or group priors. Self transitions are exactly zero because retaining a pose into itself is algebraically irrelevant and zero avoids latching.

For the generated model (`7c48b4fbd137425589323e27abedbfed88edc5b25293936c7951da7a9e0c7d61`), the held-out transition-window MSE changed as follows:

| Group | Static current-viseme pose | Context model | Relative improvement |
|---|---:|---:|---:|
| Jaw | 3.360739 | 3.011367 | 10.396% |
| Lips | 3.476935 | 3.223286 | 7.295% |
| Tongue tip | 3.446754 | 3.009692 | 12.680% |
| Tongue body | 3.307383 | 2.955528 | 10.638% |
| Overall | 3.396022 | 3.063115 | 9.803% |

The selected training/development data contains 13,325 viseme transition events; 193 of 225 transition cells occur, and 166 meet the direct-estimate threshold of 12 samples. Every cell ships its sample count and backoff level.

## What the table can and cannot infer

The transition table itself adds evidence-based coarticulation timing to the information VRChat already exposes. It cannot recover distinctions destroyed by the hard index, such as `/m/` versus `/p/` inside `PP` or `/n/` versus `/l/` inside `nn`. The separate hidden-phone posterior above can use face dynamics to assign a bounded compatibility probability for the `/m/` versus `/n,l/` case, but it still cannot make the distinction certain or observe tongue contact that neither VRChat nor a tracker supplies. Neither inferred path may be presented as measured native tongue tracking.

The corpus uses Kaldi forced-aligned ARPAbet labels. They are deterministically mapped as a **surrogate** for the hidden Oculus/VRChat classifier output; this is not a claim that VRChat emits phonemes or uses the same mapping. Diphthongs are expanded (`aw: aa→U`, `ay: aa→I`, `ey: E→I`, `ow: O→U`, `oy: O→I`) before fitting.
