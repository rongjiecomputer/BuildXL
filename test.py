#! python3
import os
import sys
import subprocess

from absl import flags
from absl.testing import absltest

FLAGS = flags.FLAGS
ROOT_DIR = os.path.dirname(os.path.realpath(__file__))

flags.DEFINE_string('test_bazelsandbox_dir',
                    os.path.join(ROOT_DIR, 'Out/Bin/BazelSandbox/release/win-x64'),
                    'Root directory where BazelSandbox lives')

def RunCommand(command, **kwargs):
  # print('Running command:', command)

  process = subprocess.Popen(command,
      stdout=subprocess.PIPE, stderr=subprocess.PIPE, universal_newlines=True,
      **kwargs)
  stdout, stderr = process.communicate()

  return (process.returncode, stdout, stderr)

def RunCommandInSandbox(command, sandbox_options=[], **kwargs):
  return RunCommand([os.path.join(FLAGS.test_bazelsandbox_dir, 'BazelSandbox.exe')]
                     + sandbox_options + ['--'] + command, **kwargs)

class BazelSandboxTest(absltest.TestCase):

  def assertExitStatus(self, actual_exitcode, expected_exitcode, stdout, stderr):
    if actual_exitcode != expected_exitcode:
      raise Exception(f"{actual_exitcode} != {expected_exitcode}\nstdout: {stdout}\nstderr: {stderr}")

  def test_system32(self):
    exitcode, stdout, stderr = RunCommandInSandbox(
        [r'C:\Windows\System32\cmd.exe', '/c', 'echo Hello World'])
    self.assertExitStatus(exitcode, 0, stdout, stderr)
    self.assertEqual(stdout, 'Hello World\n')

  def test_console_redirect(self):
    stdoutF = self.create_tempfile('stdout.txt')

    exitcode, stdout, stderr = RunCommandInSandbox(
      [r'C:\Windows\System32\cmd.exe', '/c', 'echo Hello World'],
      ['-l', stdoutF.full_path])
    self.assertExitStatus(exitcode, 0, stdout, stderr)
    self.assertEqual(stdout, '')
    with open(stdoutF.full_path, 'r') as f:
      self.assertEqual(f.readline(), 'Hello World\n')

  def test_param_file(self):
    param = self.create_tempfile('param.txt', '--\nC:\\Windows\\System32\\cmd.exe\n/c\necho Hello World')
    exitcode, stdout, stderr = RunCommand(
      [os.path.join(FLAGS.test_bazelsandbox_dir, 'BazelSandbox.exe'), '@' + param.full_path])
    self.assertExitStatus(exitcode, 0, stdout, stderr)
    self.assertEqual(stdout, 'Hello World\n')

    param1 = self.create_tempfile('param1.txt', '--\nC:\\Windows\\System32\\cmd.exe')
    param2 = self.create_tempfile('param2.txt', '/c\necho Hello World')    
    exitcode, stdout, stderr = RunCommand(
      [os.path.join(FLAGS.test_bazelsandbox_dir, 'BazelSandbox.exe'), '@' + param1.full_path, '@' + param2.full_path])
    self.assertExitStatus(exitcode, 0, stdout, stderr)
    self.assertEqual(stdout, 'Hello World\n')

  def test_absolute_path(self):
    options = "lLwrW"
    for opt in options:
      exitcode, stdout, stderr = RunCommandInSandbox(
        [r'C:\Windows\System32\cmd.exe', '/c', 'echo Hello World'],
        ['-' + opt, 'dummy_relative_path'])
      self.assertExitStatus(exitcode, 1, stdout, stderr)
      self.assertStartsWith(stdout, "Cannot create absolute path from 'dummy_relative_path'")

  def test_timeout_values(self):
    options = "tT"
    valid = ['0', '3', '10000']
    invalid = ['', 'a', 'A', '"1"', '-1']
    for opt in options:
      for v in valid:
        exitcode, stdout, stderr = RunCommandInSandbox(
          [r'C:\Windows\System32\cmd.exe', '/c', 'echo Hello World'],
          ['-' + opt, v])
        self.assertExitStatus(exitcode, 0, stdout, stderr)
        self.assertEqual(stdout, "Hello World\n")

      for v in invalid:
        exitcode, stdout, stderr = RunCommandInSandbox(
          [r'C:\Windows\System32\cmd.exe', '/c', 'echo Hello World'],
          ['-' + opt, v])
        self.assertExitStatus(exitcode, 1, stdout, stderr)

  def test_timeout(self):
    bat = self.create_tempfile('test.bat', '@echo off\nSTART /WAIT timeout /T 5 /NOBREAK > NUL\necho Hello World')

    # timeout = 0 means no timeout at all
    exitcode, stdout, stderr = RunCommandInSandbox(
      [r'C:\Windows\System32\cmd.exe', '/c', bat.full_path],
      ['-T', '0', '-r', bat.full_path])
    self.assertExitStatus(exitcode, 0, stdout, stderr)
    self.assertEqual(stdout, 'Hello World\n')

    exitcode, stdout, stderr = RunCommandInSandbox(
      [r'C:\Windows\System32\cmd.exe', '/c', bat.full_path],
      ['-T', '1', '-r', bat.full_path])
    self.assertExitStatus(exitcode, 27021977, stdout, stderr)
    self.assertEqual(stdout, '')

  def test_empty_command(self):
    # Nothing after '--'
    exitcode, stdout, stderr = RunCommandInSandbox([])
    self.assertExitStatus(exitcode, 1, stdout, stderr)
    self.assertStartsWith(stdout, 'Command to sandboxed not specified')

    # No '--'
    exitcode, stdout, stderr = RunCommand(
      [os.path.join(FLAGS.test_bazelsandbox_dir, 'BazelSandbox.exe')])
    self.assertExitStatus(exitcode, 1, stdout, stderr)
    self.assertStartsWith(stdout, 'Command to sandboxed not specified')

  def test_working_dir(self):
    dir = self.create_tempdir()

    exitcode, stdout, stderr = RunCommandInSandbox(
      [r'C:\Windows\System32\cmd.exe', '/c', 'echo %CD%'],
      ['-W', dir.full_path])
    self.assertExitStatus(exitcode, 0, stdout, stderr)
    self.assertEqual(stdout, dir.full_path + '\n')

  def _setup(self):
    self.dir = self.create_tempdir()
    self.atxt = self.dir.create_file('a.txt', 'Sherlock Holmes')
    self.btxt = self.dir.create_file('b.txt', 'Dr. Jekyll and Mr. Hyde')
    self.ctxt = self.dir.create_file('c/c.txt')

  def test_file_access(self):
    self._setup()

    # File accesses in working dir are all blocked by default, but program can
    # still run
    exitcode, stdout, stderr = RunCommandInSandbox(
      [os.path.join(ROOT_DIR, 'test.exe')],
      ['-W', self.dir.full_path])
    self.assertExitStatus(exitcode, 0, stdout, stderr)

    self.assertContainsSubsequence(stdout.split('\n'), [
      'a.txt: failed to open for read',
      'b.txt: failed to open for read',
      'b.txt: failed to open for write',
    ])

    # But file/directory enumeration is always allowed
    exitcode, stdout, stderr = RunCommandInSandbox(
      [r'C:\Windows\System32\cmd.exe', '/c', 'dir /S /b .'],
      ['-W', self.dir.full_path])
    self.assertExitStatus(exitcode, 0, stdout, stderr)
    self.assertMultiLineEqual(stdout,
      '{0}\\a.txt\n{0}\\b.txt\n{0}\\c\n{0}\\c\\c.txt\n'.format(self.dir.full_path))

    # We can make working dir to be readonly
    exitcode, stdout, stderr = RunCommandInSandbox(
      [os.path.join(ROOT_DIR, 'test.exe')],
      ['-W', self.dir.full_path, '-r', self.dir.full_path])
    self.assertExitStatus(exitcode, 0, stdout, stderr)

    self.assertContainsSubsequence(stdout.split('\n'), [
      'a.txt: Sherlock Holmes',
      'b.txt: Dr. Jekyll and Mr. Hyde',
      'b.txt: failed to open for write',
    ])

    # We can make working dir to be read-write
    exitcode, stdout, stderr = RunCommandInSandbox(
      [os.path.join(ROOT_DIR, 'test.exe')],
      ['-W', self.dir.full_path, '-w', self.dir.full_path])
    self.assertExitStatus(exitcode, 0, stdout, stderr)

    self.assertContainsSubsequence(stdout.split('\n'), [
      'a.txt: Sherlock Holmes',
      'b.txt: Dr. Jekyll and Mr. Hyde',
      'b.txt: can open for write',
    ])

    # TODO(rongjiecomputer): Add test for conflicting permissions
    # being applied to parent paths

  def test_env(self):
    env = os.environ.copy()
    env["NAME"] = "Jarvis" # test adding new env var
    env["PATH"] = "C:\\Windows;C:\\Windows\\System32" # test modifying existing env var
    env["APPDATA"] = "" # test removing existing env var

    exitcode, stdout, stderr = RunCommandInSandbox(
      [r'C:\Windows\System32\cmd.exe', '/c', 'echo NAME=%NAME% & echo PATH=%PATH% & echo APPDATA=%APPDATA%'],
      env=env)
    self.assertExitStatus(exitcode, 0, stdout, stderr)
    self.assertMultiLineEqual(stdout,
      'NAME=Jarvis \n' +
      'PATH=C:\\Windows;C:\\Windows\\System32 \n' +
      'APPDATA=%APPDATA%\n')

  def args_test_helper(self, args):
    exitcode, stdout, stderr = RunCommandInSandbox(
      [os.path.join(ROOT_DIR, 'test.exe')] + args)
    self.assertExitStatus(exitcode, 0, stdout, stderr)

    args = [os.path.join(ROOT_DIR, 'test.exe')] + args
    expected_output = "".join(["argv: ({})\n".format(arg) for arg in args])
    self.assertStartsWith(stdout, expected_output)

  def test_args(self):
    combinations = [
      [],
      ['a', 'b'],
      ['a b', 'a\tb', 'a\vb', 'a\nb', 'a"b'],
      ['a', '', 'b'], # empty arg should be preserved
      ['^', 'a^', '^^'], # caret should have no effect with CreateProcess, unlike cmd.exe
      ['\\', '\\\\', '\\\\\\'],
      ['a\\', '\\a', '\\a\\'],
      ['"', '"a', 'b"', '"c"'],
    ]
    for c in combinations:
      self.args_test_helper(c)

if __name__ == '__main__':
  absltest.main()
