import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import train_oculus_viseme_dynamics as dynamics


class SharedModelConfigurationTests(unittest.TestCase):
    def test_configuration_restores_the_shared_halo_module(self) -> None:
        original = (
            dynamics.halo.OBSERVER_RESPONSE_SECONDS,
            dynamics.halo.DEFAULT_SPEECH_LIVELINESS,
            dynamics.halo.MAXIMUM_SPEECH_LIVELINESS_LEAD,
            dynamics.halo.EVALUATION_LIVELINESS,
            dynamics.halo.RENDER_RATE_FPS,
        )

        with self.assertRaisesRegex(RuntimeError, "training stopped"):
            with dynamics.shared_model_configuration():
                self.assertEqual(
                    dynamics.halo.OBSERVER_RESPONSE_SECONDS,
                    dynamics.OBSERVER_RESPONSE_SECONDS,
                )
                raise RuntimeError("training stopped")

        self.assertEqual(
            (
                dynamics.halo.OBSERVER_RESPONSE_SECONDS,
                dynamics.halo.DEFAULT_SPEECH_LIVELINESS,
                dynamics.halo.MAXIMUM_SPEECH_LIVELINESS_LEAD,
                dynamics.halo.EVALUATION_LIVELINESS,
                dynamics.halo.RENDER_RATE_FPS,
            ),
            original,
        )


if __name__ == "__main__":
    unittest.main()
