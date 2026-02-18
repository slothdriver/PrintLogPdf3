#### Need to know
경로(회사)
C:\Program Files (x86)\M2I Corp\TOP Design Studio\SCADA\Database\SystemLog\SystemLog.db
C:\Program Files (x86)\M2I Corp\TOP Design Studio\SCADA\Database\Alarm\GlobalAlarm.db
C:\SystemLog\SystemLog.db
C:\Alarm\GlobalAlarm.db

USB이동 설치용 명령
dotnet publish -c Release -r win-x64 --self-contained true

git사용 명령
git init 이 폴더 관리하겠다고 선언하는 명령어. 현재 폴더 안에 .git/폴더가 생김. 이 폴더가 생기는 시점부터 관리시작. 
git commit -m "" //특정 시점을 스냅샷에 추가, m에 commit 관련 내용 메모.
git add //현재 상태를 다음 스냅샷에 포함.
git log --oneline //과거시점 메모 출력
git push //내 pc에 저장된 커밋들을 서버에 업로드
git pull //서버의 최신코드를 내 pc로 가져오기.
git checkout 9bc21d -- Form1.cs //특정 커밋으로 파일 하나만 복구.
git status //수정된 파일
              //add된 파일
             //commint안한 변경 등 현재상태 출력
git config --global user.name "acatundercar" //commit시에 어떤 user이름으로 할것이냐?









