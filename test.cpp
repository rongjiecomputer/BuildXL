#ifndef _CRT_SECURE_NO_WARNINGS
#define _CRT_SECURE_NO_WARNINGS
#endif

#include <cstdio>

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

  testForRead("a.txt");
  testForRead("b.txt");

  FILE* c_txt = fopen("c/c.txt", "w");
  if (c_txt) {
    printf("c.txt: can open for write\n");
    fprintf(c_txt, "Alice in the wonderland\n");
    fclose(c_txt);
  } else {
    printf("c.txt: failed to open for write\n");
  }

  return 0;
}
