#define _GNU_SOURCE
#include <dlfcn.h>
#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ptrace.h>
#include <sys/resource.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>

static char *audit_path;

__attribute__((constructor)) static void initialize_audit(void)
{
    const char *path = getenv("MOTIF_AUDITOR_PROCESS_EVENTS");
    if (path)
        audit_path = strdup(path);
}

static void record_birth(pid_t parent, unsigned int event)
{
    int saved_errno = errno;
    unsigned long child = 0;
    int error = ptrace(PTRACE_GETEVENTMSG, parent, NULL, &child) < 0 ? errno : 0;
    if (audit_path) {
        int fd = open(audit_path, O_WRONLY | O_APPEND | O_CLOEXEC | O_NOFOLLOW);
        if (fd >= 0) {
            char record[160];
            int size = error ? snprintf(record, sizeof record, "{\"error\":%d}\n", error) :
                snprintf(record, sizeof record, "{\"parent\":%d,\"child\":%lu,\"event\":%u}\n", parent, child, event);
            int offset = 0;
            while (offset < size) {
                ssize_t count = write(fd, record + offset, (size_t)(size - offset));
                if (count < 0 && errno == EINTR)
                    continue;
                if (count <= 0)
                    break;
                offset += (int)count;
            }
            close(fd);
        }
    }
    errno = saved_errno;
}

pid_t wait4(pid_t pid, int *status, int options, struct rusage *usage)
{
    static pid_t (*next_wait4)(pid_t, int *, int, struct rusage *);
    if (!next_wait4)
        next_wait4 = dlsym(RTLD_NEXT, "wait4");
    if (!next_wait4) {
        errno = ENOSYS;
        return -1;
    }
    pid_t result = next_wait4(pid, status, options, usage);
    if (result > 0 && status && WIFSTOPPED(*status)) {
        unsigned int event = (unsigned int)*status >> 16;
        if (event == PTRACE_EVENT_FORK || event == PTRACE_EVENT_VFORK || event == PTRACE_EVENT_CLONE)
            record_birth(result, event);
    }
    return result;
}
