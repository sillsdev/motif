"""Exercise the external auditor hook with simulated kernel stops, without ptrace."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from TrialBoundary import auditor_library, process_births


@unittest.skipUnless(sys.platform == 'linux', 'The external auditor uses Linux ptrace events')
class AuditorTests(unittest.TestCase):
    def test_kernel_message_failure_is_not_ignored(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / 'events.jsonl'
            path.write_text('{"error":1}\n')
            with self.assertRaisesRegex(RuntimeError, 'PTRACE_GETEVENTMSG failed: Operation not permitted'):
                process_births(path)

    def test_native_hook_records_kernel_host_ids_and_preserves_wait_result(self):
        sample = json.loads((Path(__file__).parent / 'fixtures/nested-pidns.json').read_text())
        parents = ', '.join(row['parent'] for row in sample)
        children = ', '.join(row['child'] for row in sample)
        with tempfile.TemporaryDirectory() as root:
            root = Path(root)
            mock = root / 'kernel.c'
            mock.write_text(r'''
#define _GNU_SOURCE
#include <errno.h>
#include <signal.h>
#include <stdarg.h>
#include <sys/ptrace.h>
#include <sys/resource.h>
#include <sys/wait.h>
static int index_value;
static const int parents[] = {PARENT_VALUES};
static const unsigned long children[] = {CHILD_VALUES};
pid_t wait4(pid_t pid, int *status, int options, struct rusage *usage)
{
    (void)pid; (void)options; (void)usage;
    errno = E2BIG;
    if (index_value == 3) {
        *status = 0;
        return parents[0];
    }
    *status = ((index_value == 2 ? PTRACE_EVENT_CLONE : PTRACE_EVENT_FORK) << 16) | (SIGTRAP << 8) | 0x7f;
    return parents[index_value++];
}
long ptrace(enum __ptrace_request request, ...)
{
    if (request != PTRACE_GETEVENTMSG) {
        errno = EINVAL;
        return -1;
    }
    va_list arguments;
    va_start(arguments, request);
    (void)va_arg(arguments, pid_t);
    (void)va_arg(arguments, void *);
    unsigned long *child = va_arg(arguments, unsigned long *);
    *child = children[index_value - 1];
    va_end(arguments);
    return 0;
}
'''.replace('PARENT_VALUES', parents).replace('CHILD_VALUES', children))
            program = root / 'probe.c'
            program.write_text(r'''
#include <errno.h>
#include <stdlib.h>
#include <sys/wait.h>
int main(void)
{
    int status;
    unsetenv("MOTIF_AUDITOR_PROCESS_EVENTS");
    for (int index = 0; index < 4; index++) {
        if (wait4(-1, &status, 0, 0) <= 0 || errno != E2BIG)
            return 1;
    }
    return 0;
}
''')
            compiler = shutil.which('cc')
            for arguments in ([compiler, '-shared', '-fPIC', '-Wall', '-Wextra', '-Werror', '-o', str(root / 'kernel.so'), str(mock)],
                              [compiler, '-Wall', '-Wextra', '-Werror', '-o', str(root / 'probe'), str(program)]):
                compiled = subprocess.run(arguments, capture_output=True, stdin=subprocess.DEVNULL, timeout=30)
                self.assertEqual(0, compiled.returncode, compiled.stderr.decode(errors='replace'))
            events = root / 'events.jsonl'
            events.touch()
            env = dict(os.environ, LD_PRELOAD=str(auditor_library()) + ':' + str(root / 'kernel.so'),
                       MOTIF_AUDITOR_PROCESS_EVENTS=str(events))
            result = subprocess.run([str(root / 'probe')], env=env, capture_output=True,
                                    stdin=subprocess.DEVNULL, timeout=10)
            self.assertEqual(0, result.returncode, result.stderr.decode(errors='replace'))
            births = process_births(events)
            self.assertEqual(len(sample), len(events.read_text().splitlines()))
            for row in sample:
                self.assertEqual([row['child']], list(births[row['parent']]))
