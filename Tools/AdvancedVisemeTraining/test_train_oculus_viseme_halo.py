import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parent))
import train_oculus_viseme_halo as halo


class AuditContractTests(unittest.TestCase):
    def test_render_lead_derivation_uses_the_computed_value(self) -> None:
        expected_value = (
            halo.DEFAULT_SPEECH_LIVELINESS
            * halo.MAXIMUM_SPEECH_LIVELINESS_LEAD
        )

        self.assertEqual(
            halo.render_lead_derivation(),
            "defaultSpeechLiveliness * maximumSpeechLivelinessLead "
            f"= {expected_value:g}",
        )

    def test_cardinality_tie_break_matches_the_selection_key(self) -> None:
        smaller_cardinality = SimpleNamespace(top_k=3, halo_strength=0.9)
        smaller_strength = SimpleNamespace(top_k=5, halo_strength=0.1)

        selected = min(
            (smaller_strength, smaller_cardinality),
            key=halo.cardinality_selection_key,
        )

        self.assertIs(selected, smaller_cardinality)
        self.assertEqual(
            halo.cardinality_tie_break_description(),
            "smallest accepted TopK, then smaller h",
        )


if __name__ == "__main__":
    unittest.main()
