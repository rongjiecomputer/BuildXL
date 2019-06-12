// C++17 needed

#ifndef _CRT_SECURE_NO_WARNINGS
#define _CRT_SECURE_NO_WARNINGS
#endif

#include <cstdio>
#include <filesystem>

namespace fs = std::experimental::filesystem;

void testForRead(const char* filename) {
  char buffer[100];
  FILE* f = fopen(filename, "r");
  if (f) {
    fgets(buffer, 100, f);
    printf("%s: %s\n", filename, buffer);
    fclose(f);
  } else {
    printf("%s: failed to open for read\n", filename);
  }
}

int main(int argc, char** argv) {
  for (int i = 0; i < argc; i++) {
    printf("argv: (%s)\n", argv[i]);
  }

  printf("cwd: %ls\n", fs::current_path().c_str());

  char buffer[100] = {0};

  testForRead("a.txt");
  testForRead("b.txt");

  FILE* b_txt = fopen("b.txt", "w");
  if (b_txt) {
    printf("b.txt: can open for write\n");
    fprintf(b_txt, "Alice in the wonderland\n");
    fclose(b_txt);
  } else {
    printf("b.txt: failed to open for write\n");
  }

  return 0;
}
