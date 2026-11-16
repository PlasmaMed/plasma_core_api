// xlsx2tsv streams selected columns of worksheets inside an .xlsx (inline strings / raw values) as
// PostgreSQL COPY text rows, without extracting the archive. "[null]" cells become \N; the header row is skipped.
// Usage: xlsx2tsv <file.xlsx> <sheet2.xml,sheet3.xml,...> <A,B,AR,...>
package main

import (
	"archive/zip"
	"bufio"
	"bytes"
	"fmt"
	"html"
	"io"
	"os"
	"strings"
)

func colIndex(letters string) int {
	n := 0
	for _, ch := range letters {
		n = n*26 + int(ch-'A'+1)
	}
	return n - 1
}

// splitRows yields one "<row ...>...</row>" chunk at a time.
func splitRows(data []byte, atEOF bool) (int, []byte, error) {
	if i := bytes.Index(data, []byte("</row>")); i >= 0 {
		end := i + len("</row>")
		start := bytes.Index(data[:end], []byte("<row "))
		if start < 0 {
			return 0, nil, fmt.Errorf("row without start tag")
		}
		return end, data[start:end], nil
	}
	if atEOF {
		return len(data), nil, nil
	}
	return 0, nil, nil
}

func between(s []byte, open, close string) ([]byte, bool) {
	i := bytes.Index(s, []byte(open))
	if i < 0 {
		return nil, false
	}
	s = s[i+len(open):]
	if open == "<t" { // <t> or <t xml:space="preserve">
		gt := bytes.IndexByte(s, '>')
		if gt < 0 {
			return nil, false
		}
		if s[gt-1] == '/' {
			return []byte{}, true
		}
		s = s[gt+1:]
	}
	j := bytes.Index(s, []byte(close))
	if j < 0 {
		return nil, false
	}
	return s[:j], true
}

var escaper = strings.NewReplacer("\\", "\\\\", "\t", "\\t", "\n", "\\n", "\r", "\\r")

func main() {
	if len(os.Args) != 4 {
		fmt.Fprintln(os.Stderr, "usage: xlsx2tsv <file.xlsx> <sheets> <columns>")
		os.Exit(2)
	}
	zr, err := zip.OpenReader(os.Args[1])
	if err != nil {
		panic(err)
	}
	defer zr.Close()
	var want []int
	maxCol := 0
	for _, c := range strings.Split(os.Args[3], ",") {
		idx := colIndex(c)
		want = append(want, idx)
		if idx > maxCol {
			maxCol = idx
		}
	}
	out := bufio.NewWriterSize(os.Stdout, 1<<20)
	defer out.Flush()
	vals := make([]string, maxCol+1)
	seen := make([]bool, maxCol+1)
	for _, sheet := range strings.Split(os.Args[2], ",") {
		f, err := zr.Open("xl/worksheets/" + sheet)
		if err != nil {
			panic(err)
		}
		sc := bufio.NewScanner(bufio.NewReaderSize(f, 4<<20))
		sc.Buffer(make([]byte, 1<<20), 64<<20)
		sc.Split(splitRows)
		rows := 0
		for sc.Scan() {
			row := sc.Bytes()
			if bytes.HasPrefix(row, []byte(`<row r="1"`)) {
				continue
			}
			for i := range seen {
				seen[i] = false
			}
			rest := row
			for {
				i := bytes.Index(rest, []byte(`<c r="`))
				if i < 0 {
					break
				}
				rest = rest[i+len(`<c r="`):]
				k := 0
				for k < len(rest) && rest[k] >= 'A' && rest[k] <= 'Z' {
					k++
				}
				col := colIndex(string(rest[:k]))
				// cell body ends at "</c>" or a self-closing "/>"
				tagEnd := bytes.IndexByte(rest, '>')
				var cell []byte
				if rest[tagEnd-1] == '/' {
					cell = nil
					rest = rest[tagEnd+1:]
				} else {
					ce := bytes.Index(rest, []byte("</c>"))
					cell = rest[:ce]
					rest = rest[ce+4:]
				}
				if col > maxCol {
					continue
				}
				var v []byte
				ok := false
				if cell != nil {
					if bytes.Contains(cell[:tagEnd], []byte(`t="inlineStr"`)) {
						v, ok = between(cell[tagEnd:], "<t", "</t>")
					} else {
						v, ok = between(cell[tagEnd:], "<v>", "</v>")
					}
				}
				if ok {
					vals[col] = string(v)
				} else {
					vals[col] = ""
				}
				seen[col] = true
			}
			for n, idx := range want {
				if n > 0 {
					out.WriteByte('\t')
				}
				v := vals[idx]
				if !seen[idx] {
					v = ""
				}
				if v == "[null]" {
					out.WriteString(`\N`)
					continue
				}
				if strings.IndexByte(v, '&') >= 0 {
					v = html.UnescapeString(v)
				}
				out.WriteString(escaper.Replace(v))
			}
			out.WriteByte('\n')
			rows++
		}
		if err := sc.Err(); err != nil && err != io.EOF {
			panic(err)
		}
		f.Close()
		fmt.Fprintf(os.Stderr, "%s: %d rows\n", sheet, rows)
	}
}
