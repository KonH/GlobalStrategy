import unittest

from scripts.automation.common.ralph import (
    has_open_tasks,
    parse_prd_tasks,
    should_complete_prd,
    task_progress,
)


def build_prd(tasks_json):
    return f'''# Ralph PRD

## How this file works

- Implement the first task with `"passes": false`.
- Stop when every task has `"passes": true`.

## Tasks

```json
{tasks_json}
```
'''


class RalphPrdTests(unittest.TestCase):
    def test_progress_ignores_passes_examples_outside_task_json(self):
        prd = build_prd('[{"category": "one", "passes": true}, {"category": "two", "passes": true}]')

        self.assertEqual((2, 2, 100.0), task_progress(prd))
        self.assertFalse(has_open_tasks(prd))

    def test_progress_reports_only_real_open_tasks(self):
        prd = build_prd('[{"category": "one", "passes": true}, {"category": "two", "passes": false}]')

        self.assertEqual((1, 2, 50.0), task_progress(prd))
        self.assertTrue(has_open_tasks(prd))

    def test_invalid_task_shape_fails_fast(self):
        prd = build_prd('[{"category": "one", "passes": "true"}]')

        with self.assertRaisesRegex(RuntimeError, "boolean 'passes' field"):
            parse_prd_tasks(prd)

    def test_partial_verified_progress_still_runs_complete_prd(self):
        self.assertTrue(should_complete_prd(skip_pull_request=False, passed_tasks=1))
        self.assertFalse(should_complete_prd(skip_pull_request=False, passed_tasks=0))
        self.assertFalse(should_complete_prd(skip_pull_request=True, passed_tasks=1))


if __name__ == "__main__":
    unittest.main()
