# This is the default target, which will be built when 
# you invoke make
.PHONY: all
all: hello

# This rule tells make how to build hello from hello.cpp
hello:
	mkdir -p ./out
	dotnet publish ./src/ds3-savebackup-onedrive.csproj -c Release -r linux-x64 --self-contained --output ./out/ -p:PublishSingleFile=True 

# This rule tells make to delete hello and hello.o
.PHONY: clean 
clean:
	rm -rf ./out